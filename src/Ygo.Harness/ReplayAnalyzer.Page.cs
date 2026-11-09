using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ygo.Harness;

/// <summary>
/// The analyzer's pages: one self-contained HTML file per replay (inline CSS and JS, the data as one embedded JSON block,
/// no network), an index ranking the most suspicious decisions across a folder, and analysis.json for scripts.
/// </summary>
public static partial class ReplayAnalyzer
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The default encoder escapes < > & ' as < and so on, so the block can sit inside a <script> safely.
        Encoder = JavaScriptEncoder.Default,
    };

    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

    /// <summary>The pictures folder as seen from the pages (relative, so no home folder shows), URL-encoded; "" without pictures.</summary>
    private static string PicsBase(Options o, string outDir)
    {
        if (o.Pics == null) return "";
        if (o.PicsAbsolute) return new Uri(SafePaths.Canonical(o.Pics) + Path.DirectorySeparatorChar).AbsoluteUri;
        var rel = Path.GetRelativePath(SafePaths.Canonical(outDir), SafePaths.Canonical(o.Pics));
        return string.Join("/", rel.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)) + "/";
    }

    // ------------------------------------------------------------ The per-replay page

    public static string Page(Analysis a, Options o, string outDir)
    {
        var data = new
        {
            file = a.File, title = a.Title, date = a.Date, result = a.Result, turns = a.Turns, version = a.Version, tag = a.Tag, mats = a.Mats,
            cardCount = a.CardCount, cardsNow = a.CardsNow, seats = a.Seats, warnings = a.Warnings, unattached = a.Unattached,
            items = a.Items, cards = a.Cards, analyzed = a.Analyzed, agreed = a.Agreed, decisions = a.Decisions.Count, seconds = Math.Round(a.Seconds, 1),
            divergedFrom = a.DivergedFrom, driftFrom = a.DriftFrom, analyzedBeforeDrift = a.AnalyzedBeforeDrift, agreedBeforeDrift = a.AgreedBeforeDrift,
            mode = o.Fast ? "fast (no planner)" : "full strength, with the planner", side = o.Side, asPlayed = o.AsPlayed, pics = PicsBase(o, outDir),
        };
        // Exception text and warnings can carry paths: the project root and the home folder never reach a page.
        var json = SafePaths.Scrub(JsonSerializer.Serialize(data, Json), o.Root.Length > 0 ? o.Root : SafePaths.Home);
        return Template(a.Title.Length > 0 ? a.Title : a.File, "replay", json);
    }

    // ------------------------------------------------------------ The index and analysis.json

    private sealed class TopFlag
    {
        public string S = "", K = "", T = "", Who = "", File = "", Page = "", Src = "", Kind = "", Played = "";
        public string? Bot;
        public int Id, Turn;
        public double? G;
    }

    private static List<TopFlag> Top(List<Analysis> all) =>
        all.Where(a => a.Error == null)
            .SelectMany(a => a.Decisions.Where(d => !d.Suspect).SelectMany(d => d.Flags.Select(f => new TopFlag
            {
                S = f.S, K = f.K, T = f.T, Src = f.Src, Who = d.Who, File = a.File, Page = a.Page, Id = d.Id, Turn = d.Turn, Kind = d.Kind,
                // The page's capped view: a fresh follow-up's regret compares against a line the bot never played, so it doesn't rank.
                Played = d.Played, Bot = d.Bot, G = d.Fresh && d.FollowUp ? null : d.Regret,
            })))
            .Where(f => Weight(f.S) >= 1)
            .OrderByDescending(f => Weight(f.S)).ThenByDescending(f => f.G ?? 0).ThenBy(f => f.Src == "watcher" ? 0 : 1)
            .ThenBy(f => f.K, StringComparer.Ordinal)
            .ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Id)
            .ToList();

    private static object Summary(List<Analysis> all, Options o, double seconds = 0)
    {
        var top = Top(all);
        return new
        {
            generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            mode = o.Fast ? "fast (no planner)" : "full strength, with the planner", side = o.Side, asPlayed = o.AsPlayed, seconds = Math.Round(seconds, 1),
            replays = all.Select(a => new
            {
                file = a.File, page = a.Error == null ? a.Page : null, title = a.Title, result = a.Result, turns = a.Turns, date = a.Date, tag = a.Tag,
                decisions = a.Decisions.Count, analyzed = a.Analyzed, agreed = a.Agreed, seconds = Math.Round(a.Seconds, 1), warnings = a.Warnings, error = a.Error,
                diverged = a.Diverged, divergedFrom = a.DivergedFrom, driftFrom = a.DriftFrom, analyzedBeforeDrift = a.AnalyzedBeforeDrift, agreedBeforeDrift = a.AgreedBeforeDrift,
                flags = new { high = a.Count("high"), medium = a.Count("medium"), low = a.Count("low"), info = a.Count("info") },
                seats = a.Seats.Select(s => new { name = s.Name, team = s.Team, character = s.Character, deck = s.Deck, opinion = s.Opinion }),
            }),
            top = top.Take(50),
            byKind = top.GroupBy(f => f.K).Select(g => new { kind = g.Key, count = g.Count(), severity = g.Max(f => Weight(f.S)), replays = g.Select(f => f.File).Distinct().Count() })
                .OrderByDescending(g => g.severity).ThenByDescending(g => g.count),
            byWho = top.GroupBy(f => f.Who).Select(g => new
            {
                who = g.Key, high = g.Count(f => f.S == "high"), medium = g.Count(f => f.S == "medium"), low = g.Count(f => f.S == "low"),
                disagreements = g.Count(f => f.K == "bot disagrees"),
            }).OrderByDescending(g => g.high).ThenByDescending(g => g.medium).ThenByDescending(g => g.low),
        };
    }

    public static string Index(List<Analysis> all, Options o, double seconds) =>
        Template("Replay analysis", "index", SafePaths.Scrub(JsonSerializer.Serialize(Summary(all, o, seconds), Json), o.Root.Length > 0 ? o.Root : SafePaths.Home));

    public static string SummaryJson(List<Analysis> all, Options o) =>
        SafePaths.Scrub(JsonSerializer.Serialize(Summary(all, o), new JsonSerializerOptions(Json) { WriteIndented = true }), o.Root.Length > 0 ? o.Root : SafePaths.Home);

    // ------------------------------------------------------------ HTML

    private static string Template(string title, string kind, string json)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Esc(title)).Append("</title>\n<style>").Append(Css).Append("</style></head>\n<body data-kind=\"").Append(kind).Append("\">\n");
        sb.Append("<div id=\"app\"><noscript>This page draws itself with JavaScript; the raw data is in analysis.json.</noscript></div>\n");
        sb.Append("<script type=\"application/json\" id=\"data\">").Append(json).Append("</script>\n");
        sb.Append("<script>").Append(Js).Append("</script>\n</body></html>\n");
        return sb.ToString();
    }

    private const string Css = """
:root{--bg:#f6f5f2;--panel:#ffffff;--ink:#1d1f24;--muted:#646a75;--line:#dedcd6;--soft:#efede8;--accent:#2f5fb3;
--t0:#2f5fb3;--t1:#b3432f;--high:#c0262d;--medium:#c76a00;--low:#8a7a00;--info:#6b7280;--ok:#2e7d4f;--bad:#c0262d;--chip:#f0eee9}
@media (prefers-color-scheme:dark){:root{--bg:#15171b;--panel:#1d2026;--ink:#e6e6e3;--muted:#9aa0aa;--line:#2f333b;--soft:#23262d;
--accent:#7fa6f0;--t0:#7fa6f0;--t1:#f08d7a;--high:#ff6b6b;--medium:#ffa94d;--low:#d8c84a;--info:#9aa0aa;--ok:#5fcf8c;--bad:#ff6b6b;--chip:#2a2e36}}
*{box-sizing:border-box}html,body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif}
a{color:var(--accent)}#app{max-width:1180px;margin:0 auto;padding:16px}
h1{font-size:20px;margin:4px 0 2px}h2{font-size:15px;margin:22px 0 8px}.muted{color:var(--muted)}
.meta{display:flex;flex-wrap:wrap;gap:6px 18px;margin:6px 0 10px;color:var(--muted)}.meta b{color:var(--ink);font-weight:600}
.warn{background:color-mix(in srgb,var(--medium) 14%,var(--panel));border:1px solid color-mix(in srgb,var(--medium) 40%,var(--line));padding:8px 10px;border-radius:8px;margin:6px 0}
table{border-collapse:collapse;width:100%;background:var(--panel);border:1px solid var(--line);border-radius:8px;overflow:hidden}
th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{font-weight:600;color:var(--muted);font-size:12px;background:var(--soft)}
tr:last-child td{border-bottom:0}.scroll{overflow-x:auto}
.bar{position:sticky;top:0;z-index:5;background:var(--bg);padding:8px 0;border-bottom:1px solid var(--line);display:flex;flex-wrap:wrap;gap:8px 14px;align-items:center}
.bar label{display:flex;gap:5px;align-items:center;white-space:nowrap}.bar select,.bar input[type=search]{font:inherit;padding:3px 6px;border:1px solid var(--line);border-radius:6px;background:var(--panel);color:var(--ink)}
.bar input[type=search]{width:160px}
.turn{margin:18px 0 6px;padding:6px 10px;border-left:4px solid var(--t0);background:var(--panel);border-radius:0 8px 8px 0;font-weight:600;display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap}
.turn.p1{border-left-color:var(--t1)}.ph{margin:8px 0 2px;font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
.ln{color:var(--muted);padding:1px 0 1px 12px;font-size:13px}
.d{background:var(--panel);border:1px solid var(--line);border-left:4px solid var(--line);border-radius:8px;margin:4px 0;padding:6px 10px}
.d.s-high{border-left-color:var(--high)}.d.s-medium{border-left-color:var(--medium)}.d.s-low{border-left-color:var(--low)}.d.s-info{border-left-color:var(--info)}
.d .row{display:flex;gap:8px;align-items:baseline;flex-wrap:wrap;cursor:pointer}.d .id{color:var(--muted);font-variant-numeric:tabular-nums;font-size:12px;min-width:34px}
.who{font-weight:600}.who.t0{color:var(--t0)}.who.t1{color:var(--t1)}.d .ttl{flex:1 1 260px}
.sum{margin:3px 0 0 42px;font-size:13px}.sum .k{color:var(--muted)}
.chip{display:inline-block;font-size:11px;padding:1px 7px;border-radius:999px;background:var(--chip);color:var(--ink);white-space:nowrap}
.chip.high{background:var(--high);color:#fff}.chip.medium{background:var(--medium);color:#fff}.chip.low{background:var(--low);color:#111}.chip.info{background:var(--chip);color:var(--muted)}
.chip.ok{color:var(--ok)}.chip.bad{color:var(--bad)}
.more{display:none;margin:8px 0 2px 42px}.d.open .more{display:block}
.opts{margin:0;padding:0;list-style:none}.opts li{padding:2px 6px;border-radius:5px}.opts li.p{background:color-mix(in srgb,var(--t0) 14%,transparent)}
.opts li .tag{font-size:11px;margin-left:6px;color:var(--muted)}.opts li.b .tag.bt{color:var(--accent);font-weight:600}.opts li.p .tag.pt{color:var(--ink);font-weight:600}
.why{margin:6px 0;font-size:13px;color:var(--muted)}.flags{margin:6px 0;padding:0;list-style:none}.flags li{margin:2px 0}
.board{display:grid;grid-template-columns:1fr;gap:8px;margin-top:8px}.side{border:1px solid var(--line);border-radius:8px;padding:6px 8px;background:var(--soft)}
.side h4{margin:0 0 4px;font-size:13px;display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap}.side h4 .muted{font-weight:400}
.zrow{display:flex;flex-wrap:wrap;gap:4px;margin:3px 0;align-items:flex-start}.zl{font-size:11px;color:var(--muted);min-width:64px;padding-top:4px}
.c{display:inline-flex;gap:4px;align-items:center;max-width:210px;padding:2px 6px;border:1px solid var(--line);border-radius:6px;background:var(--panel);font-size:12px}
.c.empty{color:var(--muted);border-style:dashed;min-width:28px;justify-content:center}.c.fd{border-style:dashed}.c .n{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.c .s{color:var(--muted);font-size:11px;white-space:nowrap}.c img{width:22px;height:32px;object-fit:cover;border-radius:2px}
.chain{font-size:12px;margin:4px 0}.sc{margin:6px 0;font-size:12px}.sc td,.sc th{padding:3px 6px}.sc td.n{text-align:right;font-variant-numeric:tabular-nums}
.sc tr.pl td{background:color-mix(in srgb,var(--t0) 12%,transparent)}.sc .tg{color:var(--muted);font-size:11px}.hide{display:none!important}.small{font-size:12px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:8px;margin:8px 0}.stat{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:8px 10px}
.stat b{display:block;font-size:20px}
@media (max-width:640px){.sum,.more{margin-left:0}.bar input[type=search]{width:120px}}
""";

    private const string Js = """
(function(){
const D=JSON.parse(document.getElementById('data').textContent);const app=document.getElementById('app');
const kind=document.body.dataset.kind;const W={high:3,medium:2,low:1,info:0};
function e(t,c,h){const x=document.createElement(t);if(c)x.className=c;if(h!=null)x.textContent=h;return x}
function esc(s){return String(s).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]))}
function chip(sev,txt){const c=e('span','chip '+sev,txt||sev);return c}
if(kind==='index'){index();return}
const C=D.cards||{};
function nm(code){const c=C[code];return c?c.N:(code?('#'+code):'?')}
function card(a,where){
 if(!a)return e('span','c empty','·');
 const code=a[0],f=a[1],i=C[code],x=e('span','c'+((f&1)?' fd':''));
 if(D.pics&&code){const im=document.createElement('img');im.loading='lazy';im.alt='';im.src=D.pics+code+'.jpg';
  im.onerror=function(){if(i&&i.Al&&!this.dataset.a){this.dataset.a=1;this.src=D.pics+i.Al+'.jpg'}else this.remove()};x.appendChild(im)}
 x.appendChild(e('span','n',nm(code)));
 const bits=[];if(f&1)bits.push('set');if(f&2)bits.push('def');if(f&4)bits.push('negated');if(f&8)bits.push('revealed');
 if(i&&i.T.indexOf('Monster')>=0&&(where==='m'))bits.unshift(a[2]+'/'+a[3]);if(a[4])bits.push(a[4]+' mat');
 if(bits.length)x.appendChild(e('span','s',bits.join(' ')));
 x.title=i?(i.N+' · '+i.T+(i.T.indexOf('Monster')>=0?(' · ATK '+i.A+' / DEF '+i.D):'')+'\n\n'+i.X):'';return x}
function zone(label,list,where,keepEmpty){const r=e('div','zrow');r.appendChild(e('span','zl',label));
 const items=keepEmpty?list:list.filter(Boolean);if(!items.length){r.appendChild(e('span','muted small','none'));return r}
 items.forEach(a=>r.appendChild(card(a,where)));return r}
function seatName(team){const s=(D.seats||[]).filter(s=>s.Team===team);return s.map(s=>s.Name).join(' & ')}
function board(b){const w=e('div','board');
 [1,0].forEach(p=>{const P=b.P[p],s=e('div','side');const h=e('h4');h.appendChild(e('span','who t'+p,seatName(p)));
  h.appendChild(e('span','muted','LP '+b.Lp[p]+' · Deck '+P.D+' · Extra '+P.XN+(b.Tp===p?' · turn player':'')));s.appendChild(h);
  s.appendChild(zone('Hand',P.H,'h'));s.appendChild(zone('Monsters',P.M.slice(0,5),'m',true));
  if(P.M.length>5&&P.M.slice(5).some(Boolean))s.appendChild(zone('Extra zones',P.M.slice(5),'m'));
  s.appendChild(zone('Spell/Trap',P.S.slice(0,5),'s',true));
  const fz=P.S.slice(5).filter(Boolean);if(fz.length)s.appendChild(zone('Field/Pend.',fz,'s'));
  s.appendChild(zone('GY',P.G,'g'));if(P.B.some(Boolean))s.appendChild(zone('Banished',P.B,'b'));
  if(P.X.some(Boolean))s.appendChild(zone('Extra (face-up)',P.X,'x'));w.appendChild(s)});
 if(b.Ch&&b.Ch.length){const c=e('div','chain');c.appendChild(e('b',null,'Chain: '));
  c.appendChild(document.createTextNode(b.Ch.map((l,i)=>'CL'+(i+1)+' '+nm(l.C)+' ('+seatName(l.P)+')'+(l.T.length?' targeting '+l.T.map(nm).join(', '):'')).join(' · ')));w.appendChild(c)}
 return w}
function pts(v){return v==null?'unanswerable':Math.abs(v)>=500000?'wins or avoids a loss':String(Math.round(v))}
function sc(v){return v<=-5000000?'unanswerable':Math.abs(v)>=500000?(v>0?'win':'loss'):String(Math.round(v))}
function worst(d){let m=-1,s='';(d.Flags||[]).forEach(f=>{if(W[f.S]>m){m=W[f.S];s=f.S}});return s}
// Header
app.appendChild(e('h1',null,D.title||D.file));
const meta=e('div','meta');
[['file',D.file],['date',D.date],['result',D.result],['turns',D.turns],['format',D.version?('v'+D.version+(D.tag?', tag':'')):(D.tag?'tag':'')],['mats',D.mats],
 ['second opinion',D.mode+(D.asPlayed?', as played':'')],['analysis',D.seconds+'s']].forEach(([k,v])=>{if(v===''||v==null)return;const s=e('span');s.appendChild(document.createTextNode(k+' '));s.appendChild(e('b',null,String(v)));meta.appendChild(s)});
app.appendChild(meta);
(D.warnings||[]).forEach(w=>app.appendChild(e('div','warn',w)));
const st=e('div','scroll');const t=e('table');t.innerHTML='<tr><th>Seat</th><th>Character</th><th>Deck</th><th>Second opinion</th></tr>';
(D.seats||[]).forEach(s=>{const r=t.insertRow();r.innerHTML='<td><span class="who t'+s.Team+'">'+esc(s.Name)+'</span>'+(s.Slot>=2?' <span class="muted">(partner)</span>':'')+(s.Human?' <span class="muted">(you)</span>':'')+'</td><td>'+esc(s.Character||'-')+(s.Skill?' <span class="muted">skill '+s.Skill+'</span>':'')+'</td><td>'+esc(s.Deck)+(s.Playbook?' <span class="muted">('+esc(s.Playbook)+')</span>':'')+'</td><td>'+esc(s.Opinion)+(s.Note?' <span class="muted">· '+esc(s.Note)+'</span>':'')+'</td>'});
st.appendChild(t);app.appendChild(st);
const decs=D.items.filter(i=>i.K==='d');
const fl=decs.reduce((a,d)=>{(d.Flags||[]).forEach(f=>a[f.S]=(a[f.S]||0)+1);return a},{});
app.appendChild(e('p','muted',decs.length+' decisions · '+D.analyzed+' with a second opinion, '+D.agreed+' agreed'+(D.analyzed?(' ('+Math.round(100*D.agreed/D.analyzed)+'%)'):'')+
 (D.driftFrom>=0?' · before the first rebuild (#'+D.driftFrom+'): '+D.agreedBeforeDrift+' of '+D.analyzedBeforeDrift:'')+
 ' · flags: '+(fl.high||0)+' high, '+(fl.medium||0)+' medium, '+(fl.low||0)+' low, '+(fl.info||0)+' info'));
(D.unattached||[]).forEach(f=>app.appendChild(e('div','warn','No decision of theirs that turn · '+f.S+' · '+f.K+': '+f.T)));
// Filters
const bar=e('div','bar');
function ctl(html){const l=e('label');l.innerHTML=html;bar.appendChild(l);return l.querySelector('input,select')}
const fFlag=ctl('<input type="checkbox"> flagged only');const fSev=ctl('severity <select><option value="-1">any</option><option value="0">info+</option><option value="1">low+</option><option value="2">medium+</option><option value="3">high</option></select>');
const fSide=ctl('side <select><option value="">both</option><option value="0">'+esc(seatName(0))+'</option><option value="1">'+esc(seatName(1))+'</option></select>');
const fKind=ctl('prompt <select><option value="">all</option><option>main</option><option>battle</option><option>chain</option><option>select</option><option>yesno</option><option>option</option><option>zone</option><option>position</option><option>order</option><option>other</option></select>');
const fMinor=ctl('<input type="checkbox" checked> hide minor');const fLines=ctl('<input type="checkbox" checked> play-by-play');
const fFind=ctl('<input type="search" placeholder="card name">');const shown=e('span','muted small');bar.appendChild(shown);
bar.appendChild(e('span','muted small','keys: n / p next or previous flag'));app.appendChild(bar);
const log=e('div');app.appendChild(log);
const rows=[];
D.items.forEach(it=>{
 if(it.k==='t'){const x=e('div','turn p'+it.p);x.appendChild(e('span',null,'Turn '+it.n+': '+it.w));x.appendChild(e('span','muted','LP '+it.lp[0]+' / '+it.lp[1]));x.dataset.t='turn';log.appendChild(x);rows.push({el:x,type:'turn'});return}
 if(it.k==='p'){const x=e('div','ph',it.t);log.appendChild(x);rows.push({el:x,type:'line'});return}
 if(it.k==='l'){const x=e('div','ln',it.t);log.appendChild(x);rows.push({el:x,type:'line',text:it.t.toLowerCase()});return}
 const d=it,w=worst(d),x=e('div','d'+(w?' s-'+w:''));x.id='d'+d.Id;
 const r=e('div','row');r.appendChild(e('span','id','#'+d.Id));r.appendChild(e('span','muted small','T'+d.Turn+' · '+d.Phase));
 r.appendChild(e('span','who t'+d.Team,d.Who));r.appendChild(e('span','ttl',d.Title));
 (d.Flags||[]).forEach(f=>r.appendChild(chip(f.S,f.K)));
 if(d.Regret!=null&&d.Regret>=400)r.appendChild(chip('info','regret '+pts(d.Regret)));
 if(d.Suspect)r.appendChild(chip('bad','after divergence: unverified'));
 if(d.AfterDrift)r.appendChild(chip('info','after drift'));
 if(d.Agree===true)r.appendChild(chip('ok','bot agrees'));
 x.appendChild(r);
 const s=e('div','sum');s.innerHTML='<span class="k">played</span> '+esc(d.Played||'?')+(d.Retried?' <span class="k">(after '+d.Retried+' rejected)</span>':'')+
  (d.Bot!=null&&d.Agree===false?' · <span class="k">bot</span> '+esc(d.Bot):'');x.appendChild(s);
 const m=e('div','more');x.appendChild(m);let built=false;
 r.onclick=()=>{x.classList.toggle('open');if(!built){built=true;fill(m,d)}};
 log.appendChild(x);
 const names=new Set();(d.Options||[]).forEach(o=>{if(o.C)names.add(nm(o.C).toLowerCase())});
 rows.push({el:x,type:'d',d:d,w:w,text:(d.Title+' '+d.Played+' '+(d.Bot||'')+' '+[...names].join(' ')).toLowerCase()});
});
function fill(m,d){
 if(d.Detail)m.appendChild(e('div','muted small',d.Detail));
 const ul=e('ul','opts');(d.Options||[]).forEach(o=>{const li=e('li',(o.P?'p ':'')+(o.B?'b':''));li.appendChild(document.createTextNode(o.L));
  if(o.H&&o.C)li.appendChild(e('span','tag','(hidden: '+nm(o.C)+')'));if(o.P)li.appendChild(e('span','tag pt','played'));if(o.B)li.appendChild(e('span','tag bt','bot'));ul.appendChild(li)});
 if(!(d.Options||[]).length)ul.appendChild(e('li','muted','(no listed options)'));m.appendChild(ul);
 if(d.Bot!=null)m.appendChild(e('div','why','Bot: '+d.Bot+(d.Agree?' (same as played)':'')+(d.Fresh?' · fresh memory':'')));
 if(d.Why)m.appendChild(e('div','why','Its reasoning: '+d.Why));
 if(d.Scores&&d.Scores.length){const t=e('table','sc');t.innerHTML='<tr><th>Planner: option</th><th>final</th><th>rollout</th><th>interrupted</th><th>risk</th><th>regret</th></tr>';
  d.Scores.forEach(x=>{const r=t.insertRow();if(x.P)r.className='pl';const tags=[x.P?'played':'',x.B?'bot':'',x.U?'usual':'',x.C?'chosen':''].filter(Boolean).join(', ');
   r.innerHTML='<td>'+esc(x.L)+(tags?' <span class="tg">'+tags+'</span>':'')+'</td><td class="n">'+sc(x.F)+'</td><td class="n">'+sc(x.Pl)+'</td><td class="n">'+(x.In==null?'':sc(x.In))+'</td><td class="n">'+Math.round(x.R)+'</td><td class="n">'+pts(x.G)+'</td>'});
  const w=e('div','scroll');w.appendChild(t);m.appendChild(w)}
 if(d.ScoreWhy)m.appendChild(e('div','muted small',d.ScoreWhy+(d.Regret!=null?' · regret of the played option: '+pts(d.Regret):'')+' · board points: a card in hand 350, 1 LP 1'));
 if(d.Notes&&d.Notes.length){const t=e('table','sc');t.innerHTML='<tr><th>Heuristic: option</th><th>score</th><th>ruled out</th></tr>';
  d.Notes.forEach(x=>{const r=t.insertRow();r.innerHTML='<td>'+esc(x.L)+'</td><td class="n">'+x.S+'</td><td>'+esc(x.R||'')+'</td>'});
  const w=e('div','scroll');w.appendChild(t);m.appendChild(w)}
 if((d.Flags||[]).length){const f=e('ul','flags');d.Flags.forEach(x=>{const li=e('li');li.appendChild(chip(x.S));li.appendChild(document.createTextNode(' '+x.K+': '+x.T));f.appendChild(li)});m.appendChild(f)}
 m.appendChild(board(d.Board))}
function apply(){
 const flag=fFlag.checked,sev=+fSev.value,side=fSide.value,kd=fKind.value,minor=fMinor.checked,lines=fLines.checked,q=fFind.value.trim().toLowerCase();
 const filtering=flag||sev>=0||side!==''||kd!==''||q!=='';let n=0;
 rows.forEach(r=>{let show=true;
  if(r.type==='d'){const d=r.d;const ws=(d.Flags||[]).map(f=>W[f.S]);const top=ws.length?Math.max(...ws):-1;
   if(flag&&!ws.length)show=false;if(sev>=0&&top<sev)show=false;if(side!==''&&String(d.Team)!==side)show=false;if(kd&&d.Kind!==kd)show=false;
   if(minor&&d.Minor&&!ws.some(v=>v>=1))show=false;if(q&&r.text.indexOf(q)<0)show=false;if(show)n++}
  else if(r.type==='line'){show=lines&&!filtering}
  r.el.classList.toggle('hide',!show)});
 shown.textContent='showing '+n+' of '+decs.length+' decisions'}
[fFlag,fSev,fSide,fKind,fMinor,fLines].forEach(x=>x.onchange=apply);fFind.oninput=apply;apply();
document.addEventListener('keydown',ev=>{if(ev.target.tagName==='INPUT'||ev.target.tagName==='SELECT')return;if(ev.key!=='n'&&ev.key!=='p')return;
 const fl=rows.filter(r=>r.type==='d'&&r.w&&W[r.w]>=1&&!r.el.classList.contains('hide'));if(!fl.length)return;
 const y=window.scrollY+80;let i=ev.key==='n'?fl.findIndex(r=>r.el.offsetTop>y+2):(()=>{let k=-1;fl.forEach((r,j)=>{if(r.el.offsetTop<y-2)k=j});return k})();
 if(i<0)i=ev.key==='n'?0:fl.length-1;window.scrollTo({top:fl[i].el.offsetTop-70,behavior:'smooth'})});
if(location.hash&&document.querySelector(location.hash)){const x=document.querySelector(location.hash);x.classList.remove('hide');x.querySelector('.row').click();setTimeout(()=>window.scrollTo(0,x.offsetTop-70),50)}

function index(){
 app.appendChild(e('h1',null,'Replay analysis'));
 const r=D.replays||[];const tot=r.reduce((a,x)=>{a.d+=x.decisions;a.a+=x.analyzed;a.g+=x.agreed;a.h+=x.flags.high;a.m+=x.flags.medium;a.l+=x.flags.low;return a},{d:0,a:0,g:0,h:0,m:0,l:0});
 app.appendChild(e('div','meta muted',D.generated+' · second opinion '+D.mode+(D.asPlayed?', as played':'')+' · side '+D.side+(D.seconds?(' · '+D.seconds+'s'):'')));
 const cs=e('div','cards');[['replays',r.length],['decisions',tot.d],['agreement',tot.a?Math.round(100*tot.g/tot.a)+'%':'-'],['high',tot.h],['medium',tot.m],['low',tot.l]].forEach(([k,v])=>{const s=e('div','stat');s.appendChild(e('b',null,String(v)));s.appendChild(e('span','muted',k));cs.appendChild(s)});app.appendChild(cs);
 app.appendChild(e('h2',null,'The most suspicious decisions'));
 const top=D.top||[];if(!top.length)app.appendChild(e('p','muted','No flags of low severity or above.'));else{const w=e('div','scroll'),t=e('table');t.innerHTML='<tr><th>Severity</th><th>Flag</th><th>Who</th><th>Replay</th><th>Played / bot</th></tr>';
  top.forEach(f=>{const x=t.insertRow();x.innerHTML='<td><span class="chip '+f.S+'">'+f.S+'</span></td><td><b>'+esc(f.K)+'</b><div class="muted small">'+esc(f.T)+'</div></td><td>'+esc(f.Who)+'</td><td><a href="'+encodeURI(f.Page)+'#d'+f.Id+'">'+esc(f.File)+'</a><div class="muted small">turn '+f.Turn+', decision #'+f.Id+'</div></td><td class="small">'+esc(f.Played)+(f.Bot&&f.K==='bot disagrees'?'<div class="muted">bot: '+esc(f.Bot)+(f.G!=null?' · regret '+(Math.abs(f.G)>=500000?'wins or avoids a loss':Math.round(f.G)):'')+'</div>':'')+'</td>'});
  w.appendChild(t);app.appendChild(w)}
 app.appendChild(e('h2',null,'Replays'));{const w=e('div','scroll'),t=e('table');t.innerHTML='<tr><th>Replay</th><th>Result</th><th>Decisions</th><th>Agreement</th><th>Flags</th><th>Time</th></tr>';
  r.forEach(x=>{const row=t.insertRow();row.innerHTML='<td>'+(x.page?'<a href="'+encodeURI(x.page)+'">'+esc(x.file)+'</a>':esc(x.file))+'<div class="muted small">'+esc(x.title||'')+'</div>'+(x.error?'<div class="warn small">'+esc(x.error)+'</div>':'')+(x.warnings||[]).map(w=>'<div class="muted small">'+esc(w)+'</div>').join('')+'</td><td>'+esc(x.result||'')+'<div class="muted small">'+x.turns+' turns</div></td><td>'+x.decisions+'<div class="muted small">'+x.analyzed+' judged</div></td><td>'+(x.analyzed?Math.round(100*x.agreed/x.analyzed)+'%':'-')+'</td><td>'+['high','medium','low'].map(s=>x.flags[s]?'<span class="chip '+s+'">'+x.flags[s]+' '+s+'</span> ':'').join('')+'</td><td>'+x.seconds+'s</td>'});
  w.appendChild(t);app.appendChild(w)}
 app.appendChild(e('h2',null,'Flags by kind'));{const t=e('table');t.innerHTML='<tr><th>Kind</th><th>Count</th><th>Replays</th></tr>';(D.byKind||[]).forEach(k=>{const x=t.insertRow();x.innerHTML='<td>'+esc(k.kind)+'</td><td>'+k.count+'</td><td>'+k.replays+'</td>'});app.appendChild(t)}
 app.appendChild(e('h2',null,'By duelist'));{const t=e('table');t.innerHTML='<tr><th>Duelist</th><th>High</th><th>Medium</th><th>Low</th><th>Bot disagrees</th></tr>';(D.byWho||[]).forEach(k=>{const x=t.insertRow();x.innerHTML='<td>'+esc(k.who)+'</td><td>'+k.high+'</td><td>'+k.medium+'</td><td>'+k.low+'</td><td>'+k.disagreements+'</td>'});app.appendChild(t)}
}
})();
""";
}
