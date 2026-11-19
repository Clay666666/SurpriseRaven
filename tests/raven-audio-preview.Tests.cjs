// Run against a generated file. The only process started is an isolated, headless browser.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {pathToFileURL} = require("node:url");
const {spawn} = require("node:child_process");
const {makePlan} = require("../tools/audio-preview/player.js");

async function verify(html, browserPath) {
    const url = pathToFileURL(path.resolve(html)).href;
    const outputDir = path.dirname(path.resolve(html));
    const browser = browserPath || [
        path.join(process.env.ProgramFiles, "Google/Chrome/Application/chrome.exe"),
        path.join(process.env["ProgramFiles(x86)"], "Microsoft/Edge/Application/msedge.exe")
    ].find(fs.existsSync);
    const profile = fs.mkdtempSync(path.join(outputDir, "browser-"));
    const proc = spawn(browser, ["--headless=new", "--remote-debugging-pipe", "--mute-audio", "--disable-background-networking",
        "--disable-sync", "--disable-extensions", "--no-first-run", "--no-default-browser-check", "--autoplay-policy=no-user-gesture-required",
        "--user-data-dir=" + profile, "about:blank"], {windowsHide:true, stdio:["ignore", "ignore", "ignore", "pipe", "pipe"]});
    const requests = new Map(), external = [], errors = [];
    let seq = 0, incoming = "", sessionId;
    const timeout = setTimeout(() => { proc.kill(); }, 60000);
    const closed = new Promise(resolve => proc.once("exit", resolve));
    function failPending(error) {
        for (const p of requests.values()) p.reject(error);
        requests.clear();
    }
    proc.on("error", failPending);
    proc.on("exit", code => failPending(new Error("Browser exited: " + code)));
    proc.stdio[4].setEncoding("utf8");
    proc.stdio[4].on("data", text => {
        incoming += text;
        let end;
        while ((end = incoming.indexOf("\0")) >= 0) {
            const msg = JSON.parse(incoming.slice(0, end)); incoming = incoming.slice(end + 1);
            if (msg.method === "Network.requestWillBeSent" && /^https?:/.test(msg.params.request.url)) external.push(msg.params.request.url);
            if (msg.method === "Runtime.exceptionThrown") errors.push(msg.params.exceptionDetails.text);
            const p = requests.get(msg.id);
            if (p) { requests.delete(msg.id); if (msg.error) p.reject(Error(msg.error.message)); else p.resolve(msg.result); }
        }
    });
    function call(method, params = {}, scoped = true) {
        const id = ++seq;
        return new Promise((resolve,reject) => {
            requests.set(id, {resolve,reject});
            proc.stdio[3].write(JSON.stringify({id, method, params, sessionId:scoped ? sessionId : undefined}) + "\0");
        });
    }
    async function evaluate(expression) {
        const r = await call("Runtime.evaluate", {expression, returnByValue:true, awaitPromise:true});
        if (r.exceptionDetails) throw Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
        return r.result.value;
    }
    async function wait(expression) {
        for (let n = 0; n < 150; n++) {
            if (await evaluate(expression)) return;
            await new Promise(resolve => setTimeout(resolve, 30));
        }
        throw Error("Browser did not reach " + expression);
    }
    try {
        const {targetId} = await call("Target.createTarget", {url:"about:blank"}, false);
        ({sessionId} = await call("Target.attachToTarget", {targetId, flatten:true}, false));
        await call("Network.enable"); await call("Runtime.enable");
        await call("Page.navigate", {url});
        await wait("document.readyState==='complete' && document.querySelectorAll('#clip option').length===22");
        const dataText = fs.readFileSync(html,"utf8").match(/<script id="pack" type="application\/json">([\s\S]*?)<\/script>/)[1];
        const pack = JSON.parse(dataText);
        const measured = await evaluate(`(async () => {
            const p = JSON.parse(document.getElementById('pack').textContent);
            const ctx = new OfflineAudioContext(2,1,44100), stats = {};
            for (const [hash, encoded] of Object.entries(p.audio)) {
                const bytes = Uint8Array.from(atob(encoded), c=>c.charCodeAt(0));
                const b = await ctx.decodeAudioData(bytes.buffer);
                const channels = Array.from({length:b.numberOfChannels}, (_,i)=>b.getChannelData(i));
                let peak=0, first=-1, last=-1;
                for (let off=0; off<b.length; off+=441) {
                    const len=Math.min(441,b.length-off); let energy=0;
                    for (const c of channels) for (let j=off;j<off+len;j++) {
                        peak=Math.max(peak, Math.abs(c[j])); energy+=c[j]*c[j];
                    }
                    if (Math.sqrt(energy/(len*channels.length)) > 10**(-45/20)) {
                        if (first<0) first=off/b.sampleRate;
                        last=(off+len)/b.sampleRate;
                    }
                }
                stats[hash]={duration:b.duration,peak,first,last};
            }
            return stats;
        })()`);
        assert.equal(Object.keys(measured).length,22);
        assert(Object.values(measured).every(c=>c.peak>0 && c.duration>0));
        const durations = Object.fromEntries(Object.entries(measured).map(([k,x])=>[k,x.duration]));
        const scenes = await evaluate("Array.from(document.querySelectorAll('#scene option'),o=>o.value)");
        const s = {mode:"scene", scene:"swap", light:"gain", seed:1234, front:"normal", entry:"0", candle:"0",
            lead:"default", opened:false, pick:0, fault:"ok"};
        const forms = ["normal","reduced","text"];
        const plan = (settings, version="candidate") => makePlan(pack, version, {...s,...settings}, durations);
        for (const version of ["baseline","candidate"]) {
            const seen = new Set(), clips = new Set();
            for (const clip of Object.keys(pack[version].clips)) {
                const p = plan({mode:"clip",clip},version);
                assert.equal(p.audio.length,1); clips.add(p.audio[0].clip);
            }
            assert.equal(clips.size,22);
            for (const scene of scenes) for (const front of forms) for (const light of ["gain","same","full"]) {
                const p = plan({scene,front,light},version);
                for (const e of p.audio) { seen.add(e.event); assert(e.at>=0 && e.gain>0); }
                if (front==="text") assert(p.audio.every(e=>e.event==="entry"));
                if (light!=="gain") assert(!p.audio.some(e=>e.event==="candle.light"));
            }
            assert.deepEqual([...seen].sort(),Object.keys(pack[version].events).sort());
            assert.equal(plan({scene:"entry",entry:"silent"},version).audio.length,0);
            assert.equal(plan({scene:"restore"},version).audio[0].event,"entry");
            for (const scene of ["idle","exit","refresh","cover"]) assert.equal(plan({scene},version).audio.length,0);
            for (const scene of ["early","legacy"]) assert.equal(plan({scene},version).audio.filter(e=>e.event==="result.swap").length,1);
            assert.equal(plan({scene:"consecutive"},version).audio.filter(e=>e.event==="result.swap").length,2);
            const swallow = plan({scene:"swallow"},version).audio;
            assert.deepEqual(swallow.map(x=>x.event),["candle.out","result.swallow"]);
            assert(Math.abs(swallow[1].at-swallow[0].at-0.3)<1e-6);
            assert.deepEqual(plan({scene:"upgrade",light:"same"},version).audio.map(x=>x.at),[4.55,4.55]);
            assert.deepEqual(plan({scene:"bonus",light:"same"},version).audio.map(x=>x.event),["result.bonus"]);
            assert.equal(plan({scene:"keep",light:"same"},version).audio.length,0);
            assert.deepEqual(plan({fault:"lead",light:"same"},version).audio.map(x=>x.event),["result.swap"]);
            assert.deepEqual(plan({fault:"caws",light:"same"},version).audio.map(x=>x.event),["reward.swap"]);
            assert.equal(plan({fault:"swapBoth",light:"same"},version).audio.length,0);
        }
        const c = plan({}), a = plan({},"baseline");
        const lead = c.audio.find(x=>x.event==="reward.swap"), caws = c.audio.find(x=>x.event==="result.swap");
        const onsetRelativeToDrop = lead.at+measured[lead.hash].first-pack.candidate.timelines.swapLight.Drop;
        const gap = caws.at+measured[caws.hash].first-lead.at-measured[lead.hash].last;
        assert(Math.abs(onsetRelativeToDrop+0.20)<=0.025);
        assert(gap>=-0.01 && gap<=0.09);
        const ignitionRatio = c.audio[0].gain / a.audio[0].gain;
        const effectGain = version => plan({scene:"upgrade",light:"same"},version).audio.find(x=>x.event==="result.upgrade_sfx").gain;
        const voiceGain = version => plan({scene:"upgrade",light:"same"},version).audio.find(x=>x.event==="result.upgrade").gain;
        const upgradeRatio = effectGain("candidate") / effectGain("baseline");
        assert(Math.abs(ignitionRatio - 1.05)<1e-6);
        assert(Math.abs(upgradeRatio - 0.90)<1e-6);
        assert.equal(voiceGain("candidate"), voiceGain("baseline"));
        assert(Math.abs(a.audio[0].at-3.22)<1e-6);
        assert(Math.abs(c.audio[0].at-2.40)<1e-6);
        assert.equal(c.audio[0].at, plan({scene:"swallow"}).audio[0].at);
        assert(!plan({scene:"interrupt"}).audio.some(e=>e.event==="result.swap"));
        assert(!plan({scene:"interrupt"},"baseline").audio.some(e=>e.event==="result.swap"));
        for (const value of ["0.10","0.30"]) assert(Math.abs(plan({lead:value}).audio[1].at+Number(value)-pack.candidate.timelines.swapLight.Drop)<1e-6);
        await evaluate("document.getElementById('playB').click()");
        await wait("document.getElementById('summary').textContent.startsWith('B · 调整后') && document.getElementById('events').children.length>2");
        await evaluate("document.getElementById('stop').click();document.getElementById('clipTab').click();document.getElementById('playA').click()");
        await wait("document.getElementById('summary').textContent.startsWith('A · 0.4.4') && document.getElementById('events').children.length===1");
        assert.deepEqual([external,errors],[[],[]]);
        const report = {offline:true,versions:2,eventsPerVersion:10,clipsPerVersion:22,distinctFiles:Object.keys(measured).length,
            sceneCount:scenes.length,frontEnds:forms.length,decoded:Object.keys(measured).length,onsetRelativeToDrop,leadToVoiceGap:gap,
            ignitionAt:c.audio[0].at,extinguishAt:plan({scene:"swallow"}).audio[0].at,ignitionRatio,upgradeRatio,ignitionGainDb:20*Math.log10(c.audio[0].gain/a.audio[0].gain),browserPlayback:true};
        fs.writeFileSync(path.join(outputDir,"audio-preview-results.json"),JSON.stringify(report,null,2)+"\n");
        console.log(report);
    } finally {
        clearTimeout(timeout);
        await Promise.race([call("Browser.close",{},false).catch(()=>{}),closed]);
        await closed;
    }
}
verify(process.argv[2],process.argv[3]).catch(error=>{ console.error(error); process.exitCode=1; });
