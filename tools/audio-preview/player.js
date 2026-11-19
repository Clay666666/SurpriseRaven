"use strict";
const names = {
    entry: "入场语音", "reward.swap": "平换奖励前奏", "result.swap": "三声鸦叫",
    "result.upgrade": "升档语音", "result.upgrade_sfx": "升档效果音",
    "result.downgrade": "降档语音", "result.bonus": "三选一语音", "result.swallow": "吞噬语音",
    "candle.light": "点灯", "candle.out": "熄灯"
};
const rounds = new Set(["swap", "legacy", "swallow", "upgrade", "downgrade", "bonus", "keep", "early", "consecutive", "interrupt"]);
const swaps = new Set(["swap", "legacy", "early", "consecutive", "interrupt"]);
const faultLabels = {
    ok: "完整素材", host: "整个播放器不可用", lead: "平换前奏不可用", caws: "三声鸦叫不可用",
    swapBoth: "两段平换音都不可用", voice: "升档语音不可用", effect: "升档效果音不可用"
};

function makePlan(pack, versionName, s, durations) {
    const v = pack[versionName];
    const markers = [], audio = [];
    const mark = (at, label) => markers.push({at, label});
    let state = s.seed >>> 0;
    function random() {
        state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
        return state / 4294967296;
    }
    function play(event, at, index = 0) {
        const e = v.events[event], clip = e.clips[index], hash = v.clips[clip];
        const gain = v.volume * (e.volume ?? 1) * 10 ** ((e.gainDb ?? 0) / 20);
        if (s.fault === "host") return 0;
        const duration = durations[hash];
        if (!Number.isFinite(duration) || duration <= 0) throw new Error("音频未解码 · " + clip);
        audio.push({event, clip, hash, at, gain, duration});
        let label = names[event];
        if (e.clips.length > 1) label += " · " + clip.split(".").pop();
        mark(at, label);
        return duration;
    }
    function entry(at) {
        const e = v.events.entry;
        const index = s.entry === "random" ? (random() < e.chance ? Math.floor(random() * e.clips.length) : -1)
            : s.entry === "silent" ? -1 : Number(s.entry);
        if (index < 0) mark(at, "入场抽取 · 静音"); else play("entry", at, index);
    }
    function candle(event, at) {
        const index = s.candle === "random" ? Math.floor(random() * v.events[event].clips.length) : Number(s.candle);
        play(event, at, index);
    }
    function round(offset, kind) {
        const isSwap = swaps.has(kind);
        const timeline = kind === "swallow" ? "swallow" : (isSwap ? "swap" : "return") + (s.light === "gain" ? "Light" : "");
        const t = v.timelines[timeline];
        const at = cue => t[cue] + offset;
        mark(at("Offer"), "投放遗物");
        if (s.front === "text") {
            mark(offset + 0.1, "文字界面显示结算 · 无回合音效");
            return offset + 1.25;
        }
        mark(at("Takeoff"), "起飞"); mark(at("Grab"), "叼走押品");
        if (kind === "swallow") {
            mark(at("Extinguish"), "所有烛火熄灭"); candle("candle.out", at("Extinguish"));
            play("result.swallow", at("SwallowVoice")); mark(at("Gone"), "渡鸦离开");
            mark(at("Finish"), "吞噬结束"); return at("Finish");
        }
        mark(at("Gone"), "渡鸦离开");
        if (t.Ignite !== undefined) { mark(at("Ignite"), "新增烛火点亮"); candle("candle.light", at("Ignite")); }
        mark(at("ReturnStart"), "渡鸦携物返回");
        if (isSwap) {
            const lead = versionName === "candidate" && s.lead !== "default" ? Number(s.lead) : v.swapSoundLead;
            const start = at("SwapSound") + v.swapSoundLead - Math.min(lead, at("Drop") - at("ReturnStart"));
            const missingLead = ["lead", "swapBoth"].includes(s.fault);
            const duration = missingLead ? 0 : play("reward.swap", start);
            if (missingLead) mark(start, "前奏不可用 · 立即尝试鸦叫");
            if (kind === "interrupt") {
                mark(start + 0.10, "重载开始");
                if (duration > 0) {
                    mark(start + duration, "上局前奏结束 · 取消续播");
                    return start + duration + 0.35;
                }
            }
            if (!["caws", "swapBoth"].includes(s.fault)) play("result.swap", start + duration);
            else mark(start + duration, "鸦叫不可用");
            if (kind === "interrupt") return start + duration + 0.35;
        }
        mark(at("Drop"), s.front === "reduced" ? "奖励开始原地淡入" : "鸟喙松开 · 奖励掉落");
        mark(at("Reveal"), "揭示结果");
        if (kind === "upgrade") {
            if (s.fault !== "voice") play("result.upgrade", at("Reveal"));
            if (s.fault !== "effect") play("result.upgrade_sfx", at("Reveal"));
        } else if (kind === "downgrade") play("result.downgrade", at("Reveal"));
        else if (kind === "bonus") play("result.bonus", at("Reveal"));
        else if (kind === "keep") mark(at("Reveal"), "原件返还 · 结果静音");
        mark(at("Finish"), "可以领取");
        if (kind === "early") {
            mark(at("Finish") + 0.01, "立即领取 · 原版覆盖页打开");
            mark(at("Finish") + 0.8, "覆盖页返回 · 继续已有音轨");
        }
        if (kind === "bonus") {
            mark(at("Finish") + 0.05, s.pick === 0 ? "取回原件" : "领取候选");
            mark(at("Finish") + 0.48, s.pick === 0 ? "领取候选" : "取回原件");
        }
        return at("Finish");
    }
    if (s.mode === "clip") {
        const event = Object.keys(v.events).find(key => v.events[key].clips.includes(s.clip));
        play(event, 0, v.events[event].clips.indexOf(s.clip));
    } else if (rounds.has(s.scene)) {
        if (s.opened) { mark(0, "打开路灯"); entry(0); }
        const end = round(0, s.scene);
        if (s.scene === "consecutive") round(end + 0.08, "swap");
    } else if (s.scene === "entry" || s.scene === "restore") {
        mark(0, s.scene === "entry" ? "打开路灯" : "重建面板 · 恢复已有烛火和奖励");
        entry(0);
    } else {
        const quiet = {
            cover: ["原版选牌或奖励覆盖路灯", "返回同一个路灯面板"],
            refresh: ["显示待领取奖励", "刷新已有烛火和奖励"],
            exit: ["主动离开", "清理面板与烛火"],
            idle: ["路灯待机", "悬停遗物", "取消或拒绝献祭"]
        };
        quiet[s.scene].forEach((label, i) => mark(i * 0.6, label));
    }
    markers.sort((a, b) => a.at - b.at);
    return {audio, markers, duration: Math.max(1.25, ...audio.map(x => x.at + x.duration), ...markers.map(x => x.at + 0.45))};
}

async function setupPlayer() {
    const $ = id => document.getElementById(id);
    const pack = JSON.parse($("pack").textContent);
    let mode = "scene", seed = crypto.getRandomValues(new Uint32Array(1))[0], context, decoding, revision = 0;
    let running = [], frame = 0;
    const buffers = {}, durations = {};
    const option = (value, text) => new Option(text, value);
    $("candle").add(option("random", "随机 · A/B 共用"));
    for (let i = 0; i < 6; i++) $("candle").add(option(i, "变体 " + (i + 1)));
    for (const [key, event] of Object.entries(pack.candidate.events)) {
        const group = document.createElement("optgroup"); group.label = names[key];
        for (const clip of event.clips) group.append(option(clip, names[key] + " · " + clip));
        $("clip").append(group);
    }
    $("clip").value = "sfx.candle_light.01";
    $("lead").options[1].text = "打包设定 · " + pack.candidate.swapSoundLead.toFixed(2) + " 秒";
    function settings() {
        return {mode, seed, scene:$("scene").value, front:$("front").value, light:$("light").value,
            entry:$("entry").value, candle:$("candle").value, lead:$("lead").value, clip:$("clip").value,
            fault: mode === "clip" ? "ok" : $("fault").value, pick:$("pick").selectedIndex, opened:$("opened").checked};
    }
    function stop() {
        revision++; cancelAnimationFrame(frame);
        for (const source of running) { try { source.stop(); } catch {} source.disconnect(); }
        running = []; $("stop").disabled = true;
    }
    function reset() {
        stop(); $("progress").value = 0; $("clock").textContent = "0.00 s"; $("now").textContent = "点击播放";
        $("events").replaceChildren();
        const s = settings(), round = rounds.has(s.scene), text = s.front === "text", swallow = s.scene === "swallow";
        $("scenes").hidden = mode !== "scene"; $("library").hidden = mode !== "clip";
        $("sceneTab").setAttribute("aria-pressed", mode === "scene"); $("clipTab").setAttribute("aria-pressed", mode === "clip");
        $("lightWrap").hidden = !round || text || swallow;
        $("candleWrap").hidden = !round || text || (!swallow && s.light !== "gain");
        $("candleLabel").textContent = swallow ? "熄灯变体" : "点灯变体";
        $("entryWrap").hidden = !(s.scene === "entry" || s.scene === "restore" || (round && s.opened));
        $("openedWrap").hidden = !round; $("pickWrap").hidden = s.scene !== "bonus";
        $("leadWrap").hidden = !swaps.has(s.scene) || text;
        let faults = ["ok", "host"];
        if (!text && swaps.has(s.scene)) faults.push("lead", "caws", "swapBoth");
        if (!text && s.scene === "upgrade") faults.push("voice", "effect");
        $("fault").replaceChildren(...faults.map(key => option(key, faultLabels[key])));
        $("fault").value = faults.includes(s.fault) ? s.fault : "ok";
        $("summary").textContent = mode === "clip" ? "两个版本均使用该事件的实际增益。" :
            "A/B 共用随机抽取结果。重新抽取可比较其他变体。音量以游戏主音量、SFX 音量均为 100% 为基准。";
    }
    function decode() {
        if (decoding) return decoding;
        decoding = Promise.all(Object.entries(pack.audio).map(async ([hash, encoded]) => {
            const bytes = Uint8Array.from(atob(encoded), c => c.charCodeAt(0));
            const buffer = await context.decodeAudioData(bytes.buffer);
            buffers[hash] = buffer; durations[hash] = buffer.duration;
        })).catch(error => { decoding = null; throw error; });
        return decoding;
    }
    async function play(versionName) {
        stop(); const id = revision;
        try {
            context ??= new AudioContext();
            $("now").textContent = "正在准备音频"; $("stop").disabled = false;
            await Promise.all([context.resume(), decode()]);
            if (id !== revision) return;
            const p = makePlan(pack, versionName, settings(), durations), start = context.currentTime + 0.06;
            const rows = p.markers.map(m => {
                const li = document.createElement("li"), t = document.createElement("time");
                li.className = "event"; t.textContent = m.at.toFixed(2) + " s"; li.append(t, document.createTextNode(m.label)); return li;
            });
            $("events").replaceChildren(...rows);
            for (const e of p.audio) {
                const source = context.createBufferSource(), gain = context.createGain();
                source.buffer = buffers[e.hash]; gain.gain.value = e.gain;
                source.connect(gain).connect(context.destination);
                source.onended = () => { source.disconnect(); gain.disconnect(); };
                source.start(start + e.at); running.push(source);
            }
            const version = versionName === "candidate" ? "B · 调整后" : "A · 0.4.4";
            $("summary").textContent = version + (p.audio.length ? "　" + p.audio.length + " 次播放" : "　静音场景") + "　总长 " + p.duration.toFixed(2) + " s";
            $("progress").max = p.duration;
            let last = -2;
            function update() {
                if (id !== revision) return;
                const now = Math.max(0, Math.min(context.currentTime - start, p.duration));
                $("clock").textContent = now.toFixed(2) + " s"; $("progress").value = now;
                const current = p.markers.findLastIndex(m => m.at <= now);
                if (current !== last) {
                    const same = p.markers.filter(m => m.at === p.markers[current]?.at).map(m => m.label);
                    $("now").textContent = same.join(" · ") || "开始播放";
                    rows.forEach((row, i) => row.classList.toggle("current", p.markers[i].at === p.markers[current]?.at)); last = current;
                }
                if (now < p.duration) frame = requestAnimationFrame(update);
                else { $("now").textContent = "播放完毕"; $("stop").disabled = true; }
            }
            update();
        } catch (error) {
            if (id === revision) { stop(); $("now").textContent = "播放失败"; $("summary").textContent = String(error); }
        }
    }
    $("sceneTab").onclick = () => { mode = "scene"; reset(); };
    $("clipTab").onclick = () => { mode = "clip"; reset(); };
    $("reroll").onclick = () => { seed = crypto.getRandomValues(new Uint32Array(1))[0]; reset(); };
    $("playA").onclick = () => play("baseline"); $("playB").onclick = () => play("candidate");
    $("stop").onclick = () => { stop(); $("now").textContent = "已停止"; };
    for (const field of document.querySelectorAll("select, input")) field.onchange = reset;
    reset();
}
if (typeof module !== "undefined" && module.exports) module.exports = {makePlan};
else setupPlayer();
