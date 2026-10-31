import React from "react";
import {
  AbsoluteFill,
  Audio,
  Easing,
  Img,
  interpolate,
  staticFile,
  useCurrentFrame,
} from "remotion";
import { C, FONT, MONO } from "./theme";

/**
 * W44 周报 · 36 秒快节奏版
 * 风格：M7 极简卡点 + M6 发布会运镜 + M34 条目盘点
 * 画面：只使用真实运行截图（public/report/app.png），不画概念 UI。
 * 事实来源：CHANGELOG v1.5.0 / v1.5.1 + MainForm / BrowserManager / SessionStore。
 */

const B = [0, 90, 270, 450, 630, 810, 1050, 1170]; // 120BPM，重拍切镜
const TOTAL = 1170;

const rise = (local: number, delay: number, dur = 18) => {
  const p = interpolate(local, [delay, delay + dur], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return {
    opacity: p,
    transform: `translateY(${(1 - p) * 18}px)`,
  } as React.CSSProperties;
};

const Scene: React.FC<{ index: number; children: React.ReactNode }> = ({ index, children }) => {
  const frame = useCurrentFrame();
  const start = B[index];
  const end = B[index + 1];
  const local = frame - start;
  if (local < -2 || local > end - start + 2) return null;
  const enter = interpolate(local, [0, 5], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const exit = interpolate(local, [end - start - 4, end - start], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const zoom = interpolate(local, [0, end - start], [1.035, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return (
    <AbsoluteFill style={{ opacity: Math.min(enter, exit), transform: `scale(${zoom})` }}>{children}</AbsoluteFill>
  );
};

const Backdrop: React.FC = () => (
  <>
    <AbsoluteFill style={{ background: "#06080D" }} />
    <Img
      src={staticFile("report/app-bg.png")}
      style={{ width: "100%", height: "100%", objectFit: "cover", opacity: 0.5 }}
    />
    <AbsoluteFill
      style={{
        background: "linear-gradient(115deg, transparent 34%, rgba(77,141,255,0.10) 50%, transparent 66%)",
      }}
    />
    <AbsoluteFill style={{ background: "radial-gradient(1000px 700px at 18% 10%, rgba(35,62,120,0.55), transparent 70%)" }} />
    <AbsoluteFill style={{ background: "radial-gradient(900px 620px at 92% 92%, rgba(77,141,255,0.16), transparent 70%)" }} />
    <AbsoluteFill style={{ background: "linear-gradient(180deg, rgba(6,8,13,0.35) 0%, rgba(6,8,13,0.95) 100%)" }} />
    <div
      style={{
        position: "absolute",
        right: -30,
        bottom: -190,
        fontFamily: FONT,
        fontSize: 520,
        fontWeight: 800,
        letterSpacing: -22,
        lineHeight: 1,
        color: "rgba(255,255,255,0.035)",
      }}
    >
      W44
    </div>
  </>
);

const Header: React.FC<{ no: string; title: string; note: string; local: number }> = ({ no, title, note, local }) => (
  <div style={{ position: "absolute", left: 110, top: 96 }}>
    <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
      <span style={{ fontFamily: MONO, fontSize: 24, color: C.accent, letterSpacing: 2 }}>{no}</span>
      <div style={{ width: 44, height: 2, background: C.accent, opacity: 0.7 }} />
      <span style={{ fontFamily: MONO, fontSize: 16, color: C.subtle, letterSpacing: 4 }}>W44</span>
    </div>
    <div style={{ marginTop: 14, fontSize: 68, fontWeight: 800, letterSpacing: -1.5, color: C.text, ...rise(local, 10, 16) }}>
      {title}
    </div>
    <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 20, 16) }}>{note}</div>
  </div>
);

/** 真实界面 + 动态高亮框 */
const AppShot: React.FC<{
  local: number;
  left: number;
  top: number;
  width: number;
  delay?: number;
  focus?: { left: string; top: string; width: string; height: string };
  focusLabel?: string;
}> = ({ local, left, top, width, delay = 30, focus, focusLabel }) => {
  const height = width * (728 / 1366);
  const p = interpolate(local, [delay, delay + 38], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const ring = 0.45 + 0.35 * Math.sin(local / 10);
  return (
    <div
      style={{
        position: "absolute",
        left,
        top,
        width,
        height,
        borderRadius: 14,
        overflow: "hidden",
        border: `1px solid ${C.border}`,
        boxShadow: "0 30px 90px rgba(0,0,0,0.65)",
        opacity: p,
        transform: `translateY(${(1 - p) * 26}px)`,
        background: C.bg2,
      }}
    >
      <Img src={staticFile("report/app.png")} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
      {focus ? (
        <>
          <div
            style={{
              position: "absolute",
              left: focus.left,
              top: focus.top,
              width: focus.width,
              height: focus.height,
              border: `2px solid ${C.accent}`,
              borderRadius: 8,
              background: "rgba(77,141,255,0.12)",
              boxShadow: `0 0 ${18 + ring * 22}px rgba(77,141,255,${0.35 + ring * 0.3})`,
            }}
          />
          {focusLabel ? (
            <div
              style={{
                position: "absolute",
                right: 18,
                bottom: 16,
                padding: "8px 14px",
                borderRadius: 999,
                background: "rgba(6,8,13,0.82)",
                border: `1px solid ${C.accent2}`,
                fontSize: 15,
                color: C.text,
              }}
            >
              {focusLabel}
            </div>
          ) : null}
        </>
      ) : null}
    </div>
  );
};

const Hero: React.FC = () => {
  const local = useCurrentFrame();
  return (
    <Scene index={0}>
      <div style={{ position: "absolute", left: 110, top: 260, width: 720 }}>
        <div style={{ fontFamily: MONO, fontSize: 20, color: C.accent, letterSpacing: 5, ...rise(local, 2, 10) }}>
          2026 · W44 · 10.25—10.31
        </div>
        <div style={{ marginTop: 16, fontSize: 150, fontWeight: 800, letterSpacing: -5, lineHeight: 1, color: C.text, ...rise(local, 6, 14) }}>
          本周更新
        </div>
        <div style={{ marginTop: 18, display: "flex", gap: 16, ...rise(local, 12, 12) }}>
          {["7 次提交", "9 项修复", "v1.5.1"].map((t) => (
            <span
              key={t}
              style={{
                padding: "9px 16px",
                borderRadius: 999,
                border: `1px solid ${C.border}`,
                background: "rgba(255,255,255,0.04)",
                fontSize: 20,
                color: C.text,
              }}
            >
              {t}
            </span>
          ))}
        </div>
        <div style={{ marginTop: 16, fontSize: 22, color: C.subtle, ...rise(local, 18, 12) }}>
          全部用真实运行界面，不讲概念稿
        </div>
      </div>

      <AppShot local={local} left={840} top={260} width={1040} delay={6} />
    </Scene>
  );
};

const Sidebar: React.FC = () => {
  const local = useCurrentFrame() - B[1];
  return (
    <Scene index={1}>
      <Header no="01" title="侧栏与会话" note="会话按项目归档，点开即恢复对应网页会话" local={local} />
      <div style={{ position: "absolute", left: 110, top: 380, width: 560 }}>
        {[
          ["会话列表", "按项目分组，显示时间与轮次"],
          ["搜索会话", "输入关键词直接过滤"],
          ["技能目录", "优先加载本机技能文件"],
          ["详情 / 设置 / PowerShell", "运行状态与工具入口"],
        ].map(([k, v], i) => (
          <div
            key={k}
            style={{
              padding: "24px 0",
              borderBottom: `1px solid ${C.border}`,
              ...rise(local, 22 + i * 14, 14),
            }}
          >
            <div style={{ fontSize: 29, fontWeight: 700, color: C.text }}>{k}</div>
            <div style={{ marginTop: 6, fontSize: 19, color: C.subtle }}>{v}</div>
          </div>
        ))}
      </div>

      <AppShot
        local={local}
        left={720}
        top={210}
        width={1160}
        delay={30}
        focus={{ left: "0%", top: "0%", width: "22%", height: "100%" }}
        focusLabel="左栏：会话 / 技能"
      />
    </Scene>
  );
};

const HiddenWeb: React.FC = () => {
  const local = useCurrentFrame() - B[2];
  return (
    <Scene index={2}>
      <Header no="02" title="网页后台隐藏" note="默认后台运行，需要时才显示" local={local} />
      <div style={{ position: "absolute", left: 110, top: 420, width: 620 }}>
        {[
          ["默认隐藏", "DeepSeek 网页在后台运行，不抢焦点"],
          ["显示网页会话", "点开才以独立窗口弹出"],
          ["watchdog 守护", "窗口被关闭后自动恢复连接"],
        ].map(([k, v], i) => (
          <div key={k} style={{ marginBottom: 26, ...rise(local, 24 + i * 16, 14) }}>
            <div style={{ fontSize: 28, fontWeight: 700, color: C.text }}>{k}</div>
            <div style={{ marginTop: 6, fontSize: 19, color: C.subtle }}>{v}</div>
          </div>
        ))}
      </div>

      <AppShot
        local={local}
        left={760}
        top={240}
        width={1060}
        delay={26}
        focus={{ left: "24%", top: "6%", width: "74%", height: "70%" }}
        focusLabel="真实网页会话区域"
      />
    </Scene>
  );
};

const InputFixes: React.FC = () => {
  const local = useCurrentFrame() - B[3];
  return (
    <Scene index={3}>
      <Header no="03" title="输入不再卡住" note="v1.5.1 的重点修复" local={local} />
      <div style={{ position: "absolute", left: 110, top: 400, width: 760 }}>
        {[
          ["三级兜底", "textarea → contenteditable → role=textbox，过滤不可见节点"],
          ["发送不再静默", "按钮找不到就立即报错并兜底回车"],
          ["附件顺序", "先确认输入可用，再上传附件"],
          ["Ctrl+Enter", "按焦点分派：对话页发送，会话页接续"],
        ].map(([k, v], i) => (
          <div
            key={k}
            style={{
              display: "flex",
              gap: 18,
              alignItems: "baseline",
              padding: "16px 0",
              borderBottom: `1px solid ${C.border}`,
              ...rise(local, 20 + i * 14, 14),
            }}
          >
            <span style={{ fontFamily: MONO, fontSize: 15, color: C.accent, minWidth: 110 }}>{k}</span>
            <span style={{ fontSize: 22, color: C.text }}>{v}</span>
          </div>
        ))}
      </div>

      <AppShot
        local={local}
        left={880}
        top={400}
        width={960}
        delay={24}
        focus={{ left: "24%", top: "80%", width: "74%", height: "16%" }}
        focusLabel="输入卡片"
      />
    </Scene>
  );
};

const MultiTask: React.FC = () => {
  const local = useCurrentFrame() - B[4];
  const rows = [
    ["会话绑定", "每轮流式输出写回发起时的会话，不再串台"],
    ["队列切页", "排队消息按归属会话切回对应网页再执行"],
    ["红色急停", "中断当前 PowerShell 任务，保留后续消息"],
  ];
  return (
    <Scene index={4}>
      <Header no="04" title="多任务不再串台" note="会话、队列、急停三处收口" local={local} />
      <div style={{ position: "absolute", left: 110, top: 400, width: 1700 }}>
        {rows.map(([k, v], i) => (
          <div
            key={k}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 30,
              padding: "36px 30px",
              marginBottom: 18,
              borderRadius: 14,
              background: "rgba(255,255,255,0.035)",
              border: `1px solid ${C.border}`,
              ...rise(local, 16 + i * 16, 14),
            }}
          >
            <span
              style={{
                fontFamily: MONO,
                fontSize: 14,
                letterSpacing: 2,
                color: i === 2 ? "#F87171" : C.accent,
                padding: "7px 12px",
                borderRadius: 8,
                border: `1px solid ${i === 2 ? "rgba(248,113,113,0.5)" : C.accent2}`,
                minWidth: 96,
                textAlign: "center",
              }}
            >
              {k}
            </span>
            <span style={{ fontSize: 28, color: C.text }}>{v}</span>
          </div>
        ))}
      </div>

      <div
        style={{
          position: "absolute",
          left: 110,
          top: 900,
          fontFamily: MONO,
          fontSize: 18,
          letterSpacing: 1,
          color: C.subtle,
          ...rise(local, 60),
        }}
      >
        以上修复均已进入 v1.5.1，下一轮用连续跑测确认边界状态
      </div>
    </Scene>
  );
};

const Retro: React.FC = () => {
  const local = useCurrentFrame() - B[5];
  return (
    <Scene index={5}>
      <Header no="05" title="复盘与下周" note="问题不回避，下周按优先级收口" local={local} />
      <div style={{ position: "absolute", left: 110, top: 420, width: 760 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12, ...rise(local, 14) }}>
          <span style={{ width: 9, height: 9, borderRadius: 5, background: "#FBBF24" }} />
          <span style={{ fontFamily: MONO, fontSize: 16, letterSpacing: 4, color: "#FBBF24" }}>ISSUES</span>
        </div>
        {[
          "输入框与 watchdog 仍有边界状态需要连续跑测",
          "多会话与队列刚修复，缺少长时间稳定性验证",
          "网页关闭后的恢复路径还需更多真机场景",
        ].map((t, i) => (
          <div
            key={t}
            style={{ padding: "24px 0", borderBottom: `1px solid ${C.border}`, fontSize: 27, color: C.text, ...rise(local, 22 + i * 18, 14) }}
          >
            <span style={{ color: "#FBBF24", marginRight: 14, fontFamily: MONO, fontSize: 14 }}>!</span>
            {t}
          </div>
        ))}
      </div>

      <div style={{ position: "absolute", left: 960, top: 420, width: 780 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12, ...rise(local, 18) }}>
          <span style={{ width: 9, height: 9, borderRadius: 5, background: C.accent }} />
          <span style={{ fontFamily: MONO, fontSize: 16, letterSpacing: 4, color: C.accent }}>NEXT</span>
        </div>
        {["连续跑测：多会话、队列、重启恢复", "详情页补充运行状态与诊断信息", "宣传片投放，回收首轮数据"].map((t, i) => (
          <div
            key={t}
            style={{
              display: "flex",
              gap: 18,
              alignItems: "center",
              padding: "24px 0",
              borderBottom: `1px solid ${C.border}`,
              fontSize: 27,
              color: C.text,
              ...rise(local, 30 + i * 16, 14),
            }}
          >
            <span style={{ fontFamily: MONO, fontSize: 14, color: C.accent }}>0{i + 1}</span>
            {t}
          </div>
        ))}
      </div>

      <div
        style={{
          position: "absolute",
          left: 110,
          top: 900,
          fontFamily: MONO,
          fontSize: 18,
          letterSpacing: 1,
          color: C.accent,
          ...rise(local, 60),
        }}
      >
        本周主线：输入链路与多任务绑定收口，v1.5.1 已发布
      </div>
    </Scene>
  );
};

const Outro: React.FC = () => {
  const local = useCurrentFrame() - B[6];
  return (
    <Scene index={6}>
      <div style={{ position: "absolute", left: 110, top: 430 }}>
        <div style={{ fontFamily: MONO, fontSize: 18, color: C.accent, letterSpacing: 5, ...rise(local, 6) }}>W44 · 2026</div>
        <div style={{ marginTop: 16, fontSize: 120, fontWeight: 800, letterSpacing: -4, color: C.text, ...rise(local, 12, 16) }}>
          下周见
        </div>
        <div style={{ marginTop: 12, fontSize: 24, color: C.subtle, ...rise(local, 22) }}>全部改动都在真实界面里跑过</div>
      </div>
      <div
        style={{
          position: "absolute",
          right: 110,
          bottom: 110,
          fontFamily: MONO,
          fontSize: 16,
          color: C.subtle,
          ...rise(local, 30),
        }}
      >
        github.com/Meversation/Deepseek-Desktop-Agent
      </div>
    </Scene>
  );
};

export const WeeklyReport: React.FC = () => (
  <AbsoluteFill style={{ backgroundColor: "#06080D", fontFamily: FONT }}>
    <Backdrop />
    <Hero />
    <Sidebar />
    <HiddenWeb />
    <InputFixes />
    <MultiTask />
    <Retro />
    <Outro />
    <Audio
      src={staticFile("music/techno-120.mp3")}
      volume={(f) =>
        interpolate(f, [0, 14, TOTAL - 18, TOTAL - 1], [0, 1, 1, 0], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        })
      }
    />
  </AbsoluteFill>
);

export const WEEKLY_TOTAL = TOTAL;
