using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StealthEye.Contract;
using StealthEye.Runtime;

namespace StealthEye.Tools;

public sealed class EyeLiveTool(EyeLiveSnapshotService snapshots)
{
    public EyeLiveSnapshotResult Open() => snapshots.Snapshot();
}

public sealed class EyeLiveAppTool(
    EyeLiveSnapshotService snapshots,
    EyeDispatcher dispatcher)
{
    public EyeLiveSnapshotResult Refresh() => snapshots.Snapshot();

    public async Task<EyeLiveAppActionResult> Act(EyeLiveAppActionArgs request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (effectClass, operation, args) = request.Action switch
        {
            "engine.restart" => (EyeEffectClass.Change, "engine.restart", (object?)null),
            "engine.rollback" => (EyeEffectClass.Change, "engine.rollback", (object?)null),
            "job.cancel" when !string.IsNullOrWhiteSpace(request.JobId) =>
                (EyeEffectClass.Run, "job.cancel", new JobIdArgs(request.JobId)),
            "job.cancel" => throw new ArgumentException("job_id is required for job.cancel.", nameof(request)),
            "mission.chat_associate" when
                !string.IsNullOrWhiteSpace(request.MissionId) &&
                !string.IsNullOrWhiteSpace(request.ChatRef) =>
                (EyeEffectClass.Change, "mission.chat_associate", new MissionChatAssociateArgs(
                    request.MissionId,
                    request.ChatRef,
                    request.Role,
                    request.Available)),
            "mission.chat_associate" => throw new ArgumentException(
                "mission_id and chat_ref are required for mission.chat_associate.",
                nameof(request)),
            "mission.chat_remove" when
                !string.IsNullOrWhiteSpace(request.MissionId) &&
                !string.IsNullOrWhiteSpace(request.ChatRef) =>
                (EyeEffectClass.Change, "mission.chat_remove", new MissionChatRemoveArgs(
                    request.MissionId,
                    request.ChatRef)),
            "mission.chat_remove" => throw new ArgumentException(
                "mission_id and chat_ref are required for mission.chat_remove.",
                nameof(request)),
            _ => throw new ArgumentException($"Unsupported Eye Live action: {request.Action}", nameof(request))
        };

        var response = JsonSerializer.SerializeToElement(await dispatcher.ExecuteAsync(
            effectClass,
            operation,
            args is null ? null : JsonSerializer.SerializeToElement(args)));

        if (!response.GetProperty("ok").GetBoolean())
        {
            var error = response.GetProperty("error");
            var message = error.TryGetProperty("message", out var text)
                ? text.GetString()
                : "Eye Live action failed.";
            throw new InvalidOperationException(message);
        }

        return new EyeLiveAppActionResult(request.Action, true, snapshots.Snapshot());
    }
}

public static class EyeLiveMcp
{
    public const string ResourceUri = "ui://stealtheye/live";
    public const string ResourceMimeType = "text/html;profile=mcp-app";

    public static IReadOnlyList<McpServerTool> CreateAppTools()
    {
        return
        [
            CreateAppTool(
                typeof(EyeLiveAppTool).GetMethod(nameof(EyeLiveAppTool.Refresh), BindingFlags.Instance | BindingFlags.Public)!,
                "eye_live_refresh",
                "Refresh Eye Live state.",
                readOnly: true),
            CreateAppTool(
                typeof(EyeLiveAppTool).GetMethod(nameof(EyeLiveAppTool.Act), BindingFlags.Instance | BindingFlags.Public)!,
                "eye_live_action",
                "Perform a bounded Eye Live recovery or job-cancel action.",
                readOnly: false)
        ];
    }

    private static McpServerTool CreateAppTool(
        MethodInfo method,
        string name,
        string description,
        bool readOnly)
    {
        return McpServerTool.Create(
            method,
            context => (context.Services
                ?? throw new InvalidOperationException("MCP request services are unavailable."))
                .GetRequiredService<EyeLiveAppTool>(),
            new McpServerToolCreateOptions
            {
                Name = name,
                Title = name == "eye_live_refresh" ? "Eye Live Refresh" : "Eye Live Action",
                Description = description,
                ReadOnly = readOnly,
                Destructive = !readOnly,
                Idempotent = readOnly,
                OpenWorld = false,
                UseStructuredContent = true,
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject
                    {
                        ["visibility"] = new JsonArray(JsonValue.Create("app"))
                    }
                }
            });
    }

    public static McpServerTool CreateTool(EyeContractCatalog contract)
    {
        var descriptor = contract.Descriptors.Single(x => x.Name == "eye_live");
        var method = typeof(EyeLiveTool).GetMethod(nameof(EyeLiveTool.Open), BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Eye Live tool method is missing.");

        var tool = McpServerTool.Create(
            method,
            context => (context.Services
                ?? throw new InvalidOperationException("MCP request services are unavailable."))
                .GetRequiredService<EyeLiveTool>(),
            new McpServerToolCreateOptions
            {
                Name = descriptor.Name,
                Title = "Eye Live",
                Description = descriptor.Description,
                ReadOnly = true,
                Destructive = false,
                Idempotent = true,
                OpenWorld = false,
                UseStructuredContent = true,
                OutputSchema = descriptor.ResultSchema,
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject
                    {
                        ["resourceUri"] = descriptor.ResourceUri
                    }
                }
            });
        tool.ProtocolTool.InputSchema = descriptor.InputSchema
            ?? throw new InvalidOperationException("Eye Live input schema is missing.");
        return tool;
    }
}

[McpServerResourceType]
public sealed class EyeLiveResource
{
    [McpServerResource(
        UriTemplate = EyeLiveMcp.ResourceUri,
        Name = "eye_live",
        Title = "Eye Live",
        MimeType = EyeLiveMcp.ResourceMimeType)]
    public static string Read() => Html;

    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Eye Live</title>
<style>
:root{color-scheme:light dark;font-family:ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif}
*{box-sizing:border-box}
body{margin:0;background:Canvas;color:CanvasText}
.wrap{padding:14px;display:grid;gap:12px}
.top{display:flex;justify-content:space-between;align-items:center;gap:12px}
.brand{font-size:18px;font-weight:700}
.muted{opacity:.65;font-size:12px}
.actions{display:flex;gap:7px;flex-wrap:wrap}
button,.follow{font:inherit;border:1px solid color-mix(in srgb,CanvasText 22%,transparent);background:Canvas;padding:6px 10px;border-radius:7px}
button{cursor:pointer}
button:hover{background:color-mix(in srgb,CanvasText 8%,Canvas)}
button.danger{border-color:color-mix(in srgb,#c33 55%,transparent)}
.follow{min-width:180px;max-width:420px;flex:1}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:10px}
.card{border:1px solid color-mix(in srgb,CanvasText 16%,transparent);border-radius:9px;padding:11px;min-width:0}
.card h2{font-size:13px;margin:0 0 9px}
.kv{display:grid;grid-template-columns:max-content 1fr;gap:5px 9px;font-size:12px}
.kv b{font-weight:600}
.state{font-weight:700}
.error{color:#d44;white-space:pre-wrap;overflow-wrap:anywhere}
.list{display:grid;gap:6px;max-height:310px;overflow:auto}
.row{border-top:1px solid color-mix(in srgb,CanvasText 10%,transparent);padding-top:6px;font-size:12px;min-width:0}
.row:first-child{border-top:0;padding-top:0}
.line{display:flex;gap:7px;align-items:center;justify-content:space-between}
.id{font-family:ui-monospace,SFMono-Regular,Consolas,monospace;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.tail{font-family:ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap;overflow-wrap:anywhere;max-height:110px;overflow:auto;font-size:11px;opacity:.85}
.pill{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:999px;padding:1px 6px;font-size:11px;white-space:nowrap}
.empty{opacity:.55;font-size:12px}
.footer{font-size:11px;opacity:.55}
.busy{opacity:.55;pointer-events:none}
</style>
</head>
<body>
<div class="wrap" id="app">
  <div class="top">
    <div><div class="brand">Eye Live</div><div class="muted" id="machine">Connecting...</div></div>
    <div class="actions">
      <input class="follow" id="follow" maxlength="2000" placeholder="Ask ChatGPT about this state">
      <button id="send">Send</button>
      <button id="refresh">Refresh</button>
    </div>
  </div>
  <div class="grid">
    <section class="card"><h2>Engine</h2><div id="engine" class="empty">No data</div></section>
    <section class="card"><h2>Context</h2><div id="context" class="empty">No data</div></section>
    <section class="card"><h2>Missions</h2><div id="missions" class="list empty">No data</div></section>
    <section class="card"><h2>Relay</h2><div id="relay" class="list empty">No data</div></section>
    <section class="card"><h2>Chats</h2><div class="actions"><input class="follow" id="chatMission" placeholder="mission id"><input class="follow" id="chatRef" placeholder="chat ref"><input class="follow" id="chatRole" placeholder="role"><button id="associateChat">Associate</button></div><div id="chats" class="list empty">No data</div></section>
    <section class="card"><h2>Recent jobs / terminals</h2><div id="jobs" class="list empty">No data</div></section>
    <section class="card"><h2>Recent triggers</h2><div id="triggers" class="list empty">No data</div></section>
    <section class="card"><h2>Recent artifacts</h2><div id="artifacts" class="list empty">No data</div></section>
  </div>
  <div class="footer" id="updated"></div>
</div>
<script>
(()=>{
  let seq=1;
  const pending=new Map();
  const app=document.getElementById('app');
  const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const post=m=>window.parent.postMessage(m,'*');
  const request=(method,params={})=>new Promise((resolve,reject)=>{
    const id=seq++;
    pending.set(id,{resolve,reject});
    post({jsonrpc:'2.0',id,method,params});
  });
  const notify=(method,params={})=>post({jsonrpc:'2.0',method,params});
  const call=(name,args={})=>request('tools/call',{name,arguments:args});
  const structured=r=>r?.structuredContent??r?.result?.structuredContent??null;
  const setBusy=b=>app.classList.toggle('busy',b);
  const fmt=t=>t?new Date(t).toLocaleString():'';
  const rows=(items,fn)=>items?.length?items.map(fn).join(''):'<div class="empty">None</div>';
  const terminal=s=>['completed','timed_out','cancelled','failed','interrupted'].includes(s);

  function render(s){
    if(!s)return;
    document.getElementById('machine').textContent=(s.machine?.name??'')+' | '+(s.machine?.identity??'');

    const e=s.engine??{};
    document.getElementById('engine').className='kv';
    document.getElementById('engine').innerHTML=
      '<b>State</b><span class="state">'+esc(e.state)+'</span>'+
      '<b>Active</b><span>'+esc(e.active_version??'-')+'</span>'+
      '<b>Previous</b><span>'+esc(e.previous_version??'-')+'</span>'+
      '<b>Build</b><span>'+esc(e.engine_version??'-')+'</span>'+
      '<b>PID</b><span>'+esc(e.process_id??'-')+'</span>'+
      (e.last_error?'<b>Error</b><span class="error">'+esc(e.last_error)+'</span>':'')+
      '<b>Recovery</b><span class="actions"><button data-engine="restart">Restart</button><button data-engine="rollback">Rollback</button></span>';

    const c=s.context??{};
    document.getElementById('context').className='kv';
    document.getElementById('context').innerHTML=
      '<b>Missions</b><span>'+esc(c.mission_count??0)+'</span>'+
      '<b>Active jobs</b><span>'+esc(c.active_job_count??0)+'</span>'+
      '<b>Pending triggers</b><span>'+esc(c.pending_trigger_count??0)+'</span>'+
      '<b>Relay messages</b><span>'+esc(c.relay_message_count??0)+'</span>'+
      '<b>Latest mission</b><span class="id">'+esc(c.latest_mission_id??'-')+'</span>';

    document.getElementById('missions').className='list';
    document.getElementById('missions').innerHTML=rows(s.missions,m=>
      '<div class="row"><div class="line"><span class="id" title="'+esc(m.mission_id)+'">'+esc(m.mission_id)+'</span><span class="pill">r'+esc(m.revision)+'</span></div>'+
      '<div>'+esc(m.objective)+'</div>'+
      (m.next_action?'<div class="muted">Next: '+esc(m.next_action)+'</div>':'')+
      '<div class="muted">'+esc(m.relay_count)+' relay | '+esc(fmt(m.updated_at))+'</div></div>');

    document.getElementById('relay').className='list';
    document.getElementById('relay').innerHTML=rows(s.relay,r=>
      '<div class="row"><div class="line"><span>'+esc(r.source)+'</span><span class="pill">#'+esc(r.cursor)+'</span></div>'+
      '<div>'+esc(r.message)+'</div><div class="muted">'+esc(r.mission_id)+' | '+esc(fmt(r.created_at))+'</div></div>');
    document.getElementById('chats').className='list';
    document.getElementById('chats').innerHTML=rows(s.chats,ch=>
      '<div class="row"><div class="line"><span class="id">'+esc(ch.chat_ref)+'</span><span class="pill">'+esc(ch.role??'chat')+'</span></div>'+
      '<div class="muted">'+esc(ch.mission_id)+' | '+(ch.available?'available':'closed/unavailable')+' | '+esc(fmt(ch.updated_at))+'</div>'+
      '<div class="actions"><button data-remove-chat="'+esc(ch.chat_ref)+'" data-mission="'+esc(ch.mission_id)+'">Remove</button></div></div>');

    document.getElementById('jobs').className='list';
    document.getElementById('jobs').innerHTML=rows(s.jobs,j=>
      '<div class="row"><div class="line"><span class="id" title="'+esc(j.job_id)+'">'+esc(j.job_id)+'</span><span class="pill">'+esc(j.state)+'</span></div>'+
      '<div class="muted">'+esc(j.context)+(j.terminal?' | terminal':'')+(j.pid!=null?' | pid '+esc(j.pid):'')+' | '+esc(fmt(j.created_at))+'</div>'+
      (j.stdout_tail?'<div class="tail">'+esc(j.stdout_tail)+'</div>':'')+
      (j.stderr_tail?'<div class="tail error">'+esc(j.stderr_tail)+'</div>':'')+
      (!terminal(j.state)?'<div class="actions"><button class="danger" data-cancel="'+esc(j.job_id)+'">Cancel</button></div>':'')+
      (j.failure_message?'<div class="error">'+esc(j.failure_message)+'</div>':'')+'</div>');

    document.getElementById('triggers').className='list';
    document.getElementById('triggers').innerHTML=rows(s.triggers,t=>
      '<div class="row"><div class="line"><span class="id" title="'+esc(t.trigger_id)+'">'+esc(t.trigger_id)+'</span><span class="pill">'+esc(t.state)+'</span></div>'+
      '<div class="muted">'+esc(t.kind)+' | '+esc(fmt(t.created_at))+'</div>'+
      (t.file_path?'<div class="id">'+esc(t.file_path)+'</div>':'')+
      (t.failure_message?'<div class="error">'+esc(t.failure_message)+'</div>':'')+'</div>');

    document.getElementById('artifacts').className='list';
    document.getElementById('artifacts').innerHTML=rows(s.artifacts,a=>
      '<div class="row"><div class="line"><span>'+esc(a.name)+'</span><span class="pill">'+esc(a.kind)+'</span></div>'+
      '<div class="id" title="'+esc(a.artifact_id)+'">'+esc(a.artifact_id)+'</div>'+
      '<div class="muted">'+esc(a.storage_tier)+' | '+esc(a.size_bytes)+' bytes | '+esc(fmt(a.created_at))+'</div></div>');

    document.getElementById('updated').textContent='Snapshot '+fmt(s.generated_at);
  }

  async function refresh(){
    setBusy(true);
    try{
      const r=await call('eye_live_refresh',{});
      render(structured(r));
    }finally{setBusy(false);}
  }

  async function act(action,data={}){
    setBusy(true);
    try{
      const r=await call('eye_live_action',{request:Object.assign({action},data)});
      const x=structured(r);
      render(x?.snapshot??x);
    }finally{setBusy(false);}
  }

  async function associateChat(){
    const mission_id=document.getElementById('chatMission').value.trim();
    const chat_ref=document.getElementById('chatRef').value.trim();
    const role=document.getElementById('chatRole').value.trim();
    if(!mission_id||!chat_ref)return;
    await act('mission.chat_associate',{mission_id,chat_ref,role:role||null,available:true});
  }

  async function sendFollowup(){
    const el=document.getElementById('follow');
    const text=el.value.trim();
    if(!text)return;
    await request('ui/message',{role:'user',content:[{type:'text',text}]});
    el.value='';
  }

  window.addEventListener('message',ev=>{
    const m=ev.data;
    if(!m||m.jsonrpc!=='2.0')return;
    if(m.id&&pending.has(m.id)){
      const p=pending.get(m.id);
      pending.delete(m.id);
      m.error?p.reject(m.error):p.resolve(m.result);
      return;
    }
    if(m.method==='ui/notifications/tool-result')render(structured(m.params));
  });

  document.addEventListener('click',ev=>{
    const el=ev.target.closest('button');
    if(!el)return;
    if(el.id==='refresh')refresh();
    else if(el.id==='send')sendFollowup();
    else if(el.id==='associateChat')associateChat();
    else if(el.dataset.engine)act('engine.'+el.dataset.engine);
    else if(el.dataset.cancel)act('job.cancel',{job_id:el.dataset.cancel});
    else if(el.dataset.removeChat)act('mission.chat_remove',{mission_id:el.dataset.mission,chat_ref:el.dataset.removeChat});
  });

  (async()=>{
    try{
      await request('ui/initialize',{appInfo:{name:'Eye Live',version:'1.1.0'},appCapabilities:{},protocolVersion:'2026-01-26'});
      notify('ui/notifications/initialized');
      await refresh();
    }catch(e){
      document.getElementById('machine').textContent='Eye Live bridge unavailable';
      document.getElementById('engine').innerHTML='<div class="error">'+esc(e?.message??e)+'</div>';
    }
  })();
})();
</script>
</body>
</html>
""";
}