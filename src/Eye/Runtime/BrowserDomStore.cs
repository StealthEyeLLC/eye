using System.Text.Json;
using Microsoft.Data.Sqlite;
using StealthEye.Contract;

namespace StealthEye.Runtime;

public sealed class BrowserDomStore
{
    private readonly object _gate = new();
    private readonly string _connectionString;

    public BrowserDomStore(JobStore jobStore)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = jobStore.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public BrowserDomSnapshot Apply(
        BrowserTargetHandle target,
        WorkerBrowserDomResult observation)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var cursor = NextCursor(connection, transaction);

            var priorFrames = ReadFrames(connection, transaction, target.TargetId)
                .ToDictionary(x => x.CdpFrameId, StringComparer.Ordinal);
            var frameStates = new List<BrowserFrameState>(observation.Frames.Length);
            var frameMap = new Dictionary<string, FrameIdentity>(StringComparer.Ordinal);
            var seenFrames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var frame in observation.Frames)
            {
                if (string.IsNullOrWhiteSpace(frame.CdpFrameId))
                    continue;

                seenFrames.Add(frame.CdpFrameId);
                string frameId;
                long incarnation;
                if (!priorFrames.TryGetValue(frame.CdpFrameId, out var prior))
                {
                    frameId = "frame_" + Guid.NewGuid().ToString("N");
                    incarnation = 1;
                }
                else
                {
                    frameId = prior.FrameId;
                    var replaced = !prior.Active ||
                        prior.TargetIncarnation != target.Incarnation ||
                        !string.Equals(prior.LoaderId, frame.LoaderId, StringComparison.Ordinal);
                    incarnation = replaced ? prior.Incarnation + 1 : prior.Incarnation;
                }

                frameMap[frame.CdpFrameId] = new FrameIdentity(frameId, incarnation);
            }

            foreach (var frame in observation.Frames)
            {
                if (!frameMap.TryGetValue(frame.CdpFrameId, out var identity))
                    continue;

                var parentFrameId = frame.ParentFrameId is not null &&
                                    frameMap.TryGetValue(frame.ParentFrameId, out var parentIdentity)
                    ? parentIdentity.FrameId
                    : null;

                UpsertFrame(
                    connection,
                    transaction,
                    target,
                    frame,
                    identity,
                    parentFrameId,
                    observation.ObservedAt);

                frameStates.Add(new BrowserFrameState(
                    identity.FrameId,
                    identity.Incarnation,
                    parentFrameId,
                    frame.LoaderId,
                    frame.Url));
            }

            foreach (var prior in priorFrames.Values.Where(x => x.Active && !seenFrames.Contains(x.CdpFrameId)))
                MarkFrameInactive(connection, transaction, target.TargetId, prior.CdpFrameId, observation.ObservedAt);

            var priorNodes = ReadNodes(connection, transaction, target.TargetId)
                .ToDictionary(x => x.IdentityKey, StringComparer.Ordinal);
            var nodeStates = new List<BrowserNodeState>(observation.Nodes.Length);
            var nodeMap = new Dictionary<int, NodeIdentity>();
            var seenNodeKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in observation.Nodes)
            {
                var identityKey = NodeIdentityKey(node);
                seenNodeKeys.Add(identityKey);

                var frameIdentity = node.CdpFrameId is not null &&
                                    frameMap.TryGetValue(node.CdpFrameId, out var mappedFrame)
                    ? mappedFrame
                    : null;

                string nodeId;
                long incarnation;
                if (!priorNodes.TryGetValue(identityKey, out var prior))
                {
                    nodeId = "node_" + Guid.NewGuid().ToString("N");
                    incarnation = 1;
                }
                else
                {
                    nodeId = prior.NodeId;
                    var replaced = !prior.Active ||
                        prior.TargetIncarnation != target.Incarnation ||
                        prior.FrameIncarnation != frameIdentity?.Incarnation ||
                        prior.NodeType != node.NodeType ||
                        !string.Equals(prior.NodeName, node.NodeName, StringComparison.Ordinal);
                    incarnation = replaced ? prior.Incarnation + 1 : prior.Incarnation;
                }

                nodeMap[node.CdpNodeId] = new NodeIdentity(
                    identityKey,
                    nodeId,
                    incarnation,
                    frameIdentity?.FrameId,
                    frameIdentity?.Incarnation);
            }

            foreach (var node in observation.Nodes)
            {
                if (!nodeMap.TryGetValue(node.CdpNodeId, out var identity))
                    continue;

                var parentNodeId = node.ParentNodeId is int parentCdp &&
                                   nodeMap.TryGetValue(parentCdp, out var parentIdentity)
                    ? parentIdentity.NodeId
                    : null;

                UpsertNode(
                    connection,
                    transaction,
                    target,
                    node,
                    identity,
                    parentNodeId,
                    observation.ObservedAt);

                nodeStates.Add(new BrowserNodeState(
                    identity.NodeId,
                    identity.Incarnation,
                    parentNodeId,
                    identity.FrameId,
                    node.NodeType,
                    node.NodeName,
                    node.NodeValue,
                    node.Attributes));
            }

            foreach (var prior in priorNodes.Values.Where(x => x.Active && !seenNodeKeys.Contains(x.IdentityKey)))
                MarkNodeInactive(connection, transaction, target.TargetId, prior.IdentityKey, observation.ObservedAt);

            transaction.Commit();
            return new BrowserDomSnapshot(
                cursor,
                target.TargetId,
                target.Incarnation,
                observation.ObservedAt,
                observation.Truncated,
                [.. frameStates],
                [.. nodeStates]);
        }
    }

    private static string NodeIdentityKey(WorkerBrowserNodeInfo node) =>
        node.BackendNodeId > 0
            ? "backend:" + node.BackendNodeId
            : "frontend:" + node.CdpNodeId;

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS browser_dom_state (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                );
                INSERT OR IGNORE INTO browser_dom_state (key, value)
                VALUES ('observation_cursor', 0);

                CREATE TABLE IF NOT EXISTS browser_frames (
                    target_id TEXT NOT NULL,
                    cdp_frame_id TEXT NOT NULL,
                    frame_id TEXT NOT NULL UNIQUE,
                    target_incarnation INTEGER NOT NULL,
                    incarnation INTEGER NOT NULL,
                    active INTEGER NOT NULL,
                    parent_frame_id TEXT NULL,
                    loader_id TEXT NULL,
                    url TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY (target_id, cdp_frame_id)
                );

                CREATE TABLE IF NOT EXISTS browser_nodes (
                    target_id TEXT NOT NULL,
                    identity_key TEXT NOT NULL,
                    node_id TEXT NOT NULL UNIQUE,
                    target_incarnation INTEGER NOT NULL,
                    incarnation INTEGER NOT NULL,
                    active INTEGER NOT NULL,
                    frame_id TEXT NULL,
                    frame_incarnation INTEGER NULL,
                    parent_node_id TEXT NULL,
                    cdp_node_id INTEGER NOT NULL,
                    node_type INTEGER NOT NULL,
                    node_name TEXT NOT NULL,
                    node_value TEXT NOT NULL,
                    attributes_json TEXT NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY (target_id, identity_key)
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static long NextCursor(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE browser_dom_state
                SET value = value + 1
                WHERE key = 'observation_cursor';
                """;
            update.ExecuteNonQuery();
        }

        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT value
            FROM browser_dom_state
            WHERE key = 'observation_cursor';
            """;
        return (long)(read.ExecuteScalar()
            ?? throw new InvalidOperationException("Browser DOM cursor state is missing."));
    }

    private static FrameRow[] ReadFrames(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT cdp_frame_id, frame_id, target_incarnation, incarnation, active, loader_id
            FROM browser_frames
            WHERE target_id = $target_id;
            """;
        command.Parameters.AddWithValue("$target_id", targetId);
        using var reader = command.ExecuteReader();
        var rows = new List<FrameRow>();
        while (reader.Read())
        {
            rows.Add(new FrameRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return [.. rows];
    }

    private static NodeRow[] ReadNodes(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT identity_key, node_id, target_incarnation, incarnation, active,
                   frame_incarnation, node_type, node_name
            FROM browser_nodes
            WHERE target_id = $target_id;
            """;
        command.Parameters.AddWithValue("$target_id", targetId);
        using var reader = command.ExecuteReader();
        var rows = new List<NodeRow>();
        while (reader.Read())
        {
            rows.Add(new NodeRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetInt32(6),
                reader.GetString(7)));
        }
        return [.. rows];
    }

    private static void UpsertFrame(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BrowserTargetHandle target,
        WorkerBrowserFrameInfo frame,
        FrameIdentity identity,
        string? parentFrameId,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO browser_frames (
                target_id, cdp_frame_id, frame_id, target_incarnation, incarnation,
                active, parent_frame_id, loader_id, url, last_seen_utc)
            VALUES (
                $target_id, $cdp_frame_id, $frame_id, $target_incarnation, $incarnation,
                1, $parent_frame_id, $loader_id, $url, $last_seen_utc)
            ON CONFLICT(target_id, cdp_frame_id) DO UPDATE SET
                frame_id = excluded.frame_id,
                target_incarnation = excluded.target_incarnation,
                incarnation = excluded.incarnation,
                active = 1,
                parent_frame_id = excluded.parent_frame_id,
                loader_id = excluded.loader_id,
                url = excluded.url,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$target_id", target.TargetId);
        command.Parameters.AddWithValue("$cdp_frame_id", frame.CdpFrameId);
        command.Parameters.AddWithValue("$frame_id", identity.FrameId);
        command.Parameters.AddWithValue("$target_incarnation", target.Incarnation);
        command.Parameters.AddWithValue("$incarnation", identity.Incarnation);
        command.Parameters.AddWithValue("$parent_frame_id", (object?)parentFrameId ?? DBNull.Value);
        command.Parameters.AddWithValue("$loader_id", (object?)frame.LoaderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$url", frame.Url);
        command.Parameters.AddWithValue("$last_seen_utc", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void UpsertNode(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BrowserTargetHandle target,
        WorkerBrowserNodeInfo node,
        NodeIdentity identity,
        string? parentNodeId,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO browser_nodes (
                target_id, identity_key, node_id, target_incarnation, incarnation,
                active, frame_id, frame_incarnation, parent_node_id, cdp_node_id,
                node_type, node_name, node_value, attributes_json, last_seen_utc)
            VALUES (
                $target_id, $identity_key, $node_id, $target_incarnation, $incarnation,
                1, $frame_id, $frame_incarnation, $parent_node_id, $cdp_node_id,
                $node_type, $node_name, $node_value, $attributes_json, $last_seen_utc)
            ON CONFLICT(target_id, identity_key) DO UPDATE SET
                node_id = excluded.node_id,
                target_incarnation = excluded.target_incarnation,
                incarnation = excluded.incarnation,
                active = 1,
                frame_id = excluded.frame_id,
                frame_incarnation = excluded.frame_incarnation,
                parent_node_id = excluded.parent_node_id,
                cdp_node_id = excluded.cdp_node_id,
                node_type = excluded.node_type,
                node_name = excluded.node_name,
                node_value = excluded.node_value,
                attributes_json = excluded.attributes_json,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$target_id", target.TargetId);
        command.Parameters.AddWithValue("$identity_key", identity.IdentityKey);
        command.Parameters.AddWithValue("$node_id", identity.NodeId);
        command.Parameters.AddWithValue("$target_incarnation", target.Incarnation);
        command.Parameters.AddWithValue("$incarnation", identity.Incarnation);
        command.Parameters.AddWithValue("$frame_id", (object?)identity.FrameId ?? DBNull.Value);
        command.Parameters.AddWithValue("$frame_incarnation", (object?)identity.FrameIncarnation ?? DBNull.Value);
        command.Parameters.AddWithValue("$parent_node_id", (object?)parentNodeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$cdp_node_id", node.CdpNodeId);
        command.Parameters.AddWithValue("$node_type", node.NodeType);
        command.Parameters.AddWithValue("$node_name", node.NodeName);
        command.Parameters.AddWithValue("$node_value", node.NodeValue);
        command.Parameters.AddWithValue("$attributes_json", JsonSerializer.Serialize(node.Attributes));
        command.Parameters.AddWithValue("$last_seen_utc", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void MarkFrameInactive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetId,
        string cdpFrameId,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE browser_frames
            SET active = 0, last_seen_utc = $last_seen_utc
            WHERE target_id = $target_id AND cdp_frame_id = $cdp_frame_id;
            """;
        command.Parameters.AddWithValue("$target_id", targetId);
        command.Parameters.AddWithValue("$cdp_frame_id", cdpFrameId);
        command.Parameters.AddWithValue("$last_seen_utc", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void MarkNodeInactive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetId,
        string identityKey,
        DateTimeOffset observedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE browser_nodes
            SET active = 0, last_seen_utc = $last_seen_utc
            WHERE target_id = $target_id AND identity_key = $identity_key;
            """;
        command.Parameters.AddWithValue("$target_id", targetId);
        command.Parameters.AddWithValue("$identity_key", identityKey);
        command.Parameters.AddWithValue("$last_seen_utc", observedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private sealed record FrameIdentity(string FrameId, long Incarnation);

    private sealed record NodeIdentity(
        string IdentityKey,
        string NodeId,
        long Incarnation,
        string? FrameId,
        long? FrameIncarnation);

    private sealed record FrameRow(
        string CdpFrameId,
        string FrameId,
        long TargetIncarnation,
        long Incarnation,
        bool Active,
        string? LoaderId);

    private sealed record NodeRow(
        string IdentityKey,
        string NodeId,
        long TargetIncarnation,
        long Incarnation,
        bool Active,
        long? FrameIncarnation,
        int NodeType,
        string NodeName);
}