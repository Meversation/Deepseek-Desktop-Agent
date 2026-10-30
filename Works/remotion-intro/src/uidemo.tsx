import React from "react";
import { AbsoluteFill, Easing, Img, interpolate, staticFile, useCurrentFrame } from "remotion";
import { C, FONT, MONO } from "./theme";
import { useBeatPulse } from "./beat";

/* ---------------- 真实界面框架 ---------------- */

const WIN = { x: 120, y: 40, w: 1680, h: 1000 };
const TITLE_H = 44;
const TOOLBAR_H = 50;
const STATUS_H = 30;
const LEFT_W = 760;

export const UI = {
  win: WIN,
  btnStart: { x: WIN.x + 818, y: WIN.y + TITLE_H + 8, w: 96, h: 34 },
  btnSend: { x: WIN.x + 8, y: WIN.y + 716, w: 150, h: 36 },
  prompt: { x: WIN.x + 8, y: WIN.y + 578, w: LEFT_W - 16, h: 130 },
  tabPowerShell: { x: WIN.x + 190, y: WIN.y + TITLE_H + TOOLBAR_H + 18 },
  tabChat: { x: WIN.x + 158, y: WIN.y + TITLE_H + TOOLBAR_H + 18 },
  tabUndock: { x: WIN.x + LEFT_W + 188, y: WIN.y + TITLE_H + TOOLBAR_H + 18 },
};

const ToolbarBtn: React.FC<{
  text: string;
  width: number;
  accent?: boolean;
  disabled?: boolean;
  pressed?: boolean;
}> = ({ text, width, accent, disabled, pressed }) => (
  <div
    style={{
      width,
      height: 34,
      borderRadius: 6,
      background: accent ? C.accent : C.surface,
      border: `1px solid ${accent ? C.accent : C.border}`,
      color: accent ? C.bg : C.text,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      fontSize: 15,
      fontWeight: accent ? 700 : 500,
      opacity: disabled ? 0.42 : 1,
      transform: `scale(${pressed ? 0.96 : 1})`,
      boxShadow: pressed ? `0 0 22px rgba(96,165,250,0.35)` : "none",
      flexShrink: 0,
    }}
  >
    {text}
  </div>
);

const Field: React.FC<{ width: number; text: string; mono?: boolean; placeholder?: boolean }> = ({
  width,
  text,
  mono,
  placeholder,
}) => (
  <div
    style={{
      width,
      height: 34,
      borderRadius: 6,
      background: C.input,
      border: `1px solid ${C.border}`,
      color: placeholder ? C.subtle : C.text,
      display: "flex",
      alignItems: "center",
      padding: "0 10px",
      fontSize: mono ? 14 : 15,
      fontFamily: mono ? MONO : FONT,
      overflow: "hidden",
      whiteSpace: "nowrap",
      flexShrink: 0,
    }}
  >
    {text}
  </div>
);

const Radio: React.FC<{ text: string; checked: boolean }> = ({ text, checked }) => (
  <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 15, color: C.text }}>
    <div
      style={{
        width: 15,
        height: 15,
        borderRadius: 8,
        border: `1px solid ${checked ? C.accent : C.border}`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: C.input,
      }}
    >
      {checked ? <div style={{ width: 7, height: 7, borderRadius: 4, background: C.accent }} /> : null}
    </div>
    {text}
  </div>
);

const Checkbox: React.FC<{ text: string; checked: boolean }> = ({ text, checked }) => (
  <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 15, color: C.text }}>
    <div
      style={{
        width: 15,
        height: 15,
        borderRadius: 3,
        border: `1px solid ${checked ? C.accent : C.border}`,
        background: checked ? C.accent : C.input,
        color: C.bg,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 11,
        fontWeight: 800,
      }}
    >
      {checked ? "✓" : ""}
    </div>
    {text}
  </div>
);

const Tab: React.FC<{ text: string; active: boolean; width?: number }> = ({ text, active, width }) => (
  <div
    style={{
      width,
      height: 36,
      padding: width ? 0 : "0 18px",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      fontSize: 15,
      color: active ? C.text : C.subtle,
      borderBottom: `2px solid ${active ? C.accent : "transparent"}`,
      background: active ? "rgba(96,165,250,0.08)" : "transparent",
      fontWeight: active ? 600 : 400,
    }}
  >
    {text}
  </div>
);

const TitleBar: React.FC = () => (
  <div
    style={{
      height: TITLE_H,
      borderBottom: `1px solid ${C.border}`,
      display: "flex",
      alignItems: "center",
      padding: "0 12px",
      gap: 10,
    }}
  >
    <Img src={staticFile("icon.png")} style={{ width: 18, height: 18, borderRadius: 4 }} />
    <span style={{ fontSize: 15, color: C.text }}>Deepseek-Desktop-Agent</span>
    <div style={{ marginLeft: "auto", display: "flex", gap: 12, alignItems: "center" }}>
      {["—", "▢", "✕"].map((g) => (
        <span key={g} style={{ color: C.subtle, fontSize: 14, width: 26, textAlign: "center" }}>
          {g}
        </span>
      ))}
    </div>
  </div>
);

const Toolbar: React.FC<{
  pressedStart: boolean;
  startDisabled: boolean;
  hint: string;
}> = ({ pressedStart, startDisabled, hint }) => (
  <div
    style={{
      height: TOOLBAR_H,
      borderBottom: `1px solid ${C.border}`,
      display: "flex",
      alignItems: "center",
      padding: "0 12px",
      gap: 8,
    }}
  >
    <ToolbarBtn text="选择工作目录" width={96} />
    <Field width={520} text="G:\Development\EAgent\Deepseek-Desktop-Agent" mono />
    <div style={{ display: "flex", gap: 6, width: 164, justifyContent: "center" }}>
      <Radio text="新对话" checked={false} />
      <Radio text="旧对话" checked />
    </div>
    <ToolbarBtn text="开始 / 继续" width={96} accent pressed={pressedStart} disabled={startDisabled} />
    <ToolbarBtn text="停止" width={64} disabled={!startDisabled} />
    <Checkbox text="夜间模式" checked />
    <div style={{ marginLeft: 8, fontSize: 14, color: C.subtle, whiteSpace: "nowrap", overflow: "hidden" }}>
      {hint}
    </div>
  </div>
);

const StatusBar: React.FC<{ left: string; chat: number; tools: number }> = ({ left, chat, tools }) => (
  <div
    style={{
      position: "absolute",
      left: 0,
      right: 0,
      bottom: 0,
      height: STATUS_H,
      borderTop: `1px solid ${C.border}`,
      display: "flex",
      alignItems: "center",
      padding: "0 14px",
      fontSize: 14,
      color: C.subtle,
    }}
  >
    <span style={{ overflow: "hidden", whiteSpace: "nowrap" }}>{left}</span>
    <span style={{ marginLeft: "auto", whiteSpace: "nowrap" }}>
      会话 {chat} ｜ 工具 {tools}
    </span>
  </div>
);

/* ---------------- 右侧网页内容 ---------------- */

const DeepSeekPage: React.FC<{ userMsg?: string; reply?: string; active: boolean }> = ({
  userMsg,
  reply,
  active,
}) => {
  const frame = useCurrentFrame();
  return (
    <div style={{ padding: 26, fontFamily: FONT, color: "#1C2026" }}>
      <div style={{ fontSize: 22, fontWeight: 700 }}>DeepSeek</div>
      {userMsg ? (
        <div
          style={{
            marginTop: 18,
            marginLeft: "auto",
            width: "fit-content",
            maxWidth: 520,
            padding: "12px 16px",
            borderRadius: 12,
            background: "#E8EEF7",
            color: "#1C2026",
            fontSize: 16,
          }}
        >
          {userMsg}
        </div>
      ) : (
        <div style={{ marginTop: 10, fontSize: 16, color: "#626A76" }}>今天想探索什么？</div>
      )}
      {reply ? (
        <div
          style={{
            marginTop: 18,
            padding: "14px 16px",
            borderRadius: 12,
            background: "#F3F6FA",
            border: "1px solid #DFE5EC",
            fontSize: 16,
            lineHeight: 1.7,
            maxWidth: 560,
          }}
        >
          {reply}
        </div>
      ) : null}
      {!userMsg ? (
        <div
          style={{
            marginTop: 20,
            height: 74,
            borderRadius: 12,
            border: "1px solid #DADFE6",
            background: "#FFFFFF",
          }}
        />
      ) : null}
      <div
        style={{
          position: "absolute",
          right: 18,
          bottom: 16,
          fontSize: 13,
          color: active ? "#2B7FD4" : "#9AA4B2",
        }}
      >
        {active ? "页面活动 · 已内嵌" : "页面已脱离"}
      </div>
    </div>
  );
};

const LoadingPane: React.FC<{ label: string; sub: string; frame: number }> = ({ label, sub, frame }) => (
  <div
    style={{
      position: "absolute",
      inset: 0,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      gap: 16,
    }}
  >
    <div
      style={{
        width: 44,
        height: 44,
        borderRadius: 22,
        border: "3px solid rgba(96,165,250,0.2)",
        borderTopColor: C.accent,
        transform: `rotate(${(frame % 40) * 9}deg)`,
      }}
    />
    <div style={{ fontSize: 17, color: C.text }}>{label}</div>
    <div style={{ fontSize: 14, color: C.subtle }}>{sub}</div>
  </div>
);

/* ---------------- 光标 ---------------- */

const CursorArrow: React.FC<{ x: number; y: number; click: boolean }> = ({ x, y, click }) => {
  return (
    <div style={{ position: "absolute", left: x, top: y, zIndex: 30 }}>
      {click ? (
        <>
          <div
            style={{
              position: "absolute",
              left: -14,
              top: -14,
              width: 28,
              height: 28,
              borderRadius: 14,
              border: `2px solid ${C.accent}`,
              opacity: 0.8,
            }}
          />
          <div
            style={{
              position: "absolute",
              left: -3,
              top: -3,
              width: 6,
              height: 6,
              borderRadius: 3,
              background: C.accent,
            }}
          />
        </>
      ) : null}
      <svg width={24} height={30} viewBox="0 0 24 30" style={{ filter: "drop-shadow(0 2px 5px rgba(0,0,0,0.55))" }}>
        <path d="M2 1 L2 23 L8 17 L12 27 L16 25 L12 15 L21 15 Z" fill="#FFFFFF" stroke="#0B0F16" strokeWidth="1.5" />
      </svg>
    </div>
  );
};

/* ---------------- 场景：点击「开始 / 继续」 ---------------- */

export const UiStartScene: React.FC = () => {
  const frame = useCurrentFrame();
  const beat = useBeatPulse(4);
  const clicked = frame >= 45 && frame < 50;
  const started = frame >= 46;
  const pageIn = interpolate(frame, [60, 100], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const cursorX = interpolate(frame, [6, 42], [1500, UI.btnStart.x + 40], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.inOut(Easing.cubic),
  });
  const cursorY = interpolate(frame, [6, 42], [760, UI.btnStart.y + 18], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.inOut(Easing.cubic),
  });
  const push = interpolate(frame, [0, 180], [1, 1.016], {
    extrapolateRight: "clamp",
  });
  const rightEmpty = !started;
  const statusLeft = started
    ? "浏览器已启动，DeepSeek 页面已内嵌到右侧面板"
    : "已记住上次会话 16152ae2，点击开始继续；要换会话请点「切换会话」";

  return (
    <AbsoluteFill style={{ fontFamily: FONT }}>
      <div
        style={{
          position: "absolute",
          left: WIN.x,
          top: WIN.y,
          width: WIN.w,
          height: WIN.h,
          borderRadius: 10,
          background: C.bg2,
          border: `1px solid ${C.border}`,
          boxShadow: `0 40px 120px rgba(0,0,0,0.6), 0 0 ${30 + beat * 40}px rgba(96,165,250,${0.08 + beat * 0.12})`,
          overflow: "hidden",
          transform: `scale(${push})`,
          transformOrigin: "50% 50%",
        }}
      >
        <TitleBar />
        <Toolbar
          pressedStart={clicked}
          startDisabled={started}
          hint={started ? "DeepSeek 窗口已内嵌" : "已记住上次会话 16152ae2，点击开始继续…"}
        />
        <div style={{ position: "absolute", top: TITLE_H + TOOLBAR_H, left: 0, right: 0, bottom: STATUS_H, display: "flex" }}>
          <div style={{ width: LEFT_W, borderRight: `1px solid ${C.border}`, display: "flex", flexDirection: "column" }}>
            <div style={{ height: 36, display: "flex", borderBottom: `1px solid ${C.border}` }}>
              <Tab text="对话" active />
              <Tab text="PowerShell" active={false} />
              <Tab text="设置" active={false} />
            </div>
            <div
              style={{
                margin: 8,
                flex: 1,
                borderRadius: 6,
                border: `1px solid ${C.border}`,
                background: C.bg2,
                padding: 16,
                fontSize: 15,
                color: C.subtle,
                lineHeight: 1.8,
              }}
            >
              对话内容会显示在这里。
            </div>
            <div style={{ padding: "0 8px 6px", fontSize: 12, color: C.subtle }}>未选择附件</div>
            <div
              style={{
                margin: "0 8px",
                height: 130,
                borderRadius: 6,
                border: `1px solid ${C.border}`,
                background: C.input,
                padding: 10,
                fontFamily: MONO,
                fontSize: 13,
                color: C.text,
                lineHeight: 1.6,
                overflow: "hidden",
              }}
            >
              # System Prompt
              <br />
              你是编程专家，熟练掌握了 Windows PowerShell，你要成为我的顾问以模拟工具调用的形式完成开发。
              <br />
              <br />
              ## 工作目录
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "10px 8px 12px" }}>
              <ToolbarBtn text="发送 (Ctrl+Enter)" width={150} accent />
              <ToolbarBtn text="添加附件" width={80} />
              <ToolbarBtn text="清除附件" width={80} />
              <div style={{ marginLeft: "auto" }}>
                <Checkbox text="启用 Ctrl+Enter" checked />
              </div>
            </div>
          </div>
          <div style={{ flex: 1, position: "relative" }}>
            <div style={{ height: 36, display: "flex", borderBottom: `1px solid ${C.border}` }}>
              <Tab text="DeepSeek 页面" active />
              <Tab text="还原窗口" active={false} />
              <Tab text="切换会话" active={false} />
            </div>
            <div style={{ position: "absolute", top: 36, left: 0, right: 0, bottom: 0 }}>
              {rightEmpty ? (
                <div
                  style={{
                    position: "absolute",
                    inset: 0,
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "center",
                    justifyContent: "center",
                    gap: 10,
                    color: C.subtle,
                  }}
                >
                  <div style={{ fontSize: 17, color: C.text }}>尚未启动 DeepSeek 浏览器</div>
                  <div style={{ fontSize: 14 }}>选择工作目录后会自动启动并内嵌到此处</div>
                </div>
              ) : (
                <>
                  {pageIn < 0.99 ? (
                    <LoadingPane
                      frame={frame}
                      label={pageIn < 0.5 ? "正在离屏启动浏览器…" : "定位窗口并内嵌中…"}
                      sub="不显示独立窗口"
                    />
                  ) : null}
                  <div
                    style={{
                      position: "absolute",
                      inset: 0,
                      background: "#FFFFFF",
                      opacity: pageIn,
                    }}
                  >
                    <DeepSeekPage active />
                  </div>
                </>
              )}
            </div>
          </div>
        </div>
        <StatusBar left={statusLeft} chat={0} tools={0} />
      </div>
      <CursorArrow x={cursorX} y={cursorY} click={clicked} />
    </AbsoluteFill>
  );
};

/* ---------------- 场景：发送、工具执行、还原与内嵌 ---------------- */

const psScript = [
  "$ npm install remotion @remotion/cli",
  "added 251 packages in 1m",
  "$ remotion studio --port 3001",
  "Server ready · http://localhost:3001",
];

export const UiInteractScene: React.FC = () => {
  const frame = useCurrentFrame();
  const beat = useBeatPulse(4);
  const typedText = "帮我安装 Remotion 并启动 Studio";
  const typingStart = 15;
  const typingEnd = 45;
  const typedCount = Math.max(0, Math.min(typedText.length, Math.floor(((frame - typingStart) / (typingEnd - typingStart)) * typedText.length)));
  const sent = frame >= 60;

  const clickInput = frame >= 12 && frame < 17;
  const clickSend = frame >= 58 && frame < 63;
  const clickUndock = frame >= 180 && frame < 186;
  const clickDock = frame >= 210 && frame < 216;

  const activeTab: "对话" | "PowerShell" = frame >= 105 && frame < 150 ? "PowerShell" : "对话";
  const psCount = Math.max(0, Math.min(psScript.length, Math.floor((frame - 110) / 10)));
  const psDone = frame >= 148;
  const undocked = frame >= 184 && frame < 211;

  const replyVisible = frame >= 75;
  const replyDone = frame >= 165;

  const tabSwitch = frame >= 150 ? 150 : frame >= 105 ? 105 : -40;
  const panelFade = interpolate(frame, [tabSwitch, tabSwitch + 8], [0.3, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const rightSwitch = frame >= 211 ? 211 : frame >= 184 ? 184 : -40;
  const rightFade = interpolate(frame, [rightSwitch, rightSwitch + 10], [0.25, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const popIn = interpolate(frame, [184, 194], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const push = interpolate(frame, [0, 240], [1, 1.016], {
    extrapolateRight: "clamp",
  });

  const cursorPath: Array<{ f: number; x: number; y: number }> = [
    { f: 0, x: UI.btnStart.x + 40, y: UI.btnStart.y + 18 },
    { f: 12, x: UI.prompt.x + 260, y: UI.prompt.y + 60 },
    { f: 46, x: 1500, y: 700 },
    { f: 58, x: UI.btnSend.x + 60, y: UI.btnSend.y + 18 },
    { f: 100, x: UI.tabPowerShell.x, y: UI.tabPowerShell.y },
    { f: 145, x: UI.tabChat.x, y: UI.tabChat.y },
    { f: 176, x: UI.tabUndock.x, y: UI.tabUndock.y },
    { f: 206, x: UI.tabUndock.x, y: UI.tabUndock.y },
  ];
  const cx = interpolate(
    frame,
    cursorPath.map((p) => p.f),
    cursorPath.map((p) => p.x),
    { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: Easing.inOut(Easing.cubic) },
  );
  const cy = interpolate(
    frame,
    cursorPath.map((p) => p.f),
    cursorPath.map((p) => p.y),
    { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: Easing.inOut(Easing.cubic) },
  );

  const statusLeft = undocked
    ? "窗口已还原为独立窗口；点击「内嵌窗口」可重新吸附"
    : replyDone
      ? "已收到回复 · Markdown 已渲染"
      : sent
        ? "正在等待 DeepSeek 回复…"
        : "已记住上次会话 16152ae2，点击开始继续；要换会话请点「切换会话」";

  return (
    <AbsoluteFill style={{ fontFamily: FONT }}>
      <div
        style={{
          position: "absolute",
          left: WIN.x,
          top: WIN.y,
          width: WIN.w,
          height: WIN.h,
          borderRadius: 10,
          background: C.bg2,
          border: `1px solid ${C.border}`,
          boxShadow: `0 40px 120px rgba(0,0,0,0.6), 0 0 ${30 + beat * 40}px rgba(96,165,250,${0.08 + beat * 0.12})`,
          overflow: "hidden",
          transform: `scale(${push})`,
          transformOrigin: "50% 50%",
        }}
      >
        <TitleBar />
        <Toolbar pressedStart={false} startDisabled hint={sent ? "本轮已发送 · 输入框在等待期间锁定" : "已记住上次会话 16152ae2"} />
        <div style={{ position: "absolute", top: TITLE_H + TOOLBAR_H, left: 0, right: 0, bottom: STATUS_H, display: "flex" }}>
          <div style={{ width: LEFT_W, borderRight: `1px solid ${C.border}`, display: "flex", flexDirection: "column" }}>
            <div style={{ height: 36, display: "flex", borderBottom: `1px solid ${C.border}` }}>
              <Tab text="对话" active={activeTab === "对话"} />
              <Tab text="PowerShell" active={activeTab === "PowerShell"} />
              <Tab text="设置" active={false} />
            </div>

            {activeTab === "对话" ? (
              <div
                style={{
                  margin: 8,
                  flex: 1,
                  borderRadius: 6,
                  border: `1px solid ${C.border}`,
                  background: C.bg2,
                  padding: "14px 16px",
                  fontSize: 15,
                  color: C.text,
                  lineHeight: 1.75,
                  overflow: "hidden",
                  opacity: panelFade,
                }}
              >
                {!sent ? (
                  <span style={{ color: C.subtle }}>对话内容会显示在这里。</span>
                ) : (
                  <>
                    <div style={{ fontSize: 12, color: C.subtle, marginBottom: 6 }}>你</div>
                    <div style={{ marginBottom: 14 }}>{typedText}</div>
                    <div style={{ fontSize: 12, color: C.subtle, marginBottom: 6 }}>DeepSeek</div>
                    {replyVisible ? (
                      <>
                        <div>好的，我在本机执行：先安装依赖，再启动 Studio。</div>
                        {psDone ? (
                          <div
                            style={{
                              marginTop: 10,
                              padding: "10px 12px",
                              borderRadius: 6,
                              background: "#0E131B",
                              border: `1px solid ${C.border}`,
                              fontFamily: MONO,
                              fontSize: 13,
                              color: C.ok,
                            }}
                          >
                            ✓ out/DeepseekIntro.mp4 · 退出码 0
                          </div>
                        ) : (
                          <div style={{ marginTop: 10, fontSize: 13, color: C.subtle }}>
                            正在调用 PowerShell…
                          </div>
                        )}
                        {replyDone ? (
                          <div style={{ marginTop: 10, color: C.ok, fontSize: 14 }}>工具执行完成，输出已回传。</div>
                        ) : null}
                      </>
                    ) : (
                      <span style={{ color: C.subtle }}>正在思考…</span>
                    )}
                  </>
                )}
              </div>
            ) : (
              <div
                style={{
                  margin: 8,
                  flex: 1,
                  borderRadius: 6,
                  border: `1px solid ${C.border}`,
                  background: "#0E131B",
                  padding: "14px 16px",
                  fontFamily: MONO,
                  fontSize: 13,
                  color: C.text,
                  lineHeight: 2,
                  overflow: "hidden",
                  opacity: panelFade,
                }}
              >
                <div style={{ color: C.subtle, fontSize: 12, marginBottom: 6 }}>powershell · 实时输出</div>
                {psScript.slice(0, psCount).map((l, i) => (
                  <div key={l} style={{ color: i === 3 ? C.ok : i === 1 ? C.subtle : C.text }}>
                    {l}
                  </div>
                ))}
              </div>
            )}

            <div style={{ padding: "0 8px 6px", fontSize: 12, color: C.subtle }}>未选择附件</div>
            <div
              style={{
                margin: "0 8px",
                height: 130,
                borderRadius: 6,
                border: `1px solid ${C.border}`,
                background: C.input,
                padding: 10,
                fontFamily: MONO,
                fontSize: 13,
                color: C.text,
                lineHeight: 1.6,
                overflow: "hidden",
                opacity: sent && !replyDone ? 0.55 : 1,
              }}
            >
              {typedCount > 0 ? (
                <>
                  {typedText.slice(0, typedCount)}
                  <span style={{ color: C.accent }}>|</span>
                </>
              ) : (
                <span style={{ color: C.subtle }}>在此输入任务，Enter 发送 · Shift+Enter 换行</span>
              )}
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, padding: "10px 8px 12px" }}>
              <div
                style={{
                  width: 150,
                  height: 34,
                  borderRadius: 6,
                  background: sent && !replyDone ? C.border : C.accent,
                  color: sent && !replyDone ? C.subtle : C.bg,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  fontSize: 15,
                  fontWeight: 700,
                  transform: `scale(${clickSend ? 0.96 : 1})`,
                }}
              >
                {sent && !replyDone ? "等待回复…" : "发送 (Ctrl+Enter)"}
              </div>
              <ToolbarBtn text="添加附件" width={80} />
              <ToolbarBtn text="清除附件" width={80} />
              <div style={{ marginLeft: "auto" }}>
                <Checkbox text="启用 Ctrl+Enter" checked />
              </div>
            </div>
          </div>

          <div style={{ flex: 1, position: "relative" }}>
            <div style={{ height: 36, display: "flex", borderBottom: `1px solid ${C.border}` }}>
              <Tab text="DeepSeek 页面" active />
              <Tab text={undocked ? "内嵌窗口" : "还原窗口"} active={undocked} />
              <Tab text="切换会话" active={false} />
            </div>
            <div style={{ position: "absolute", top: 36, left: 0, right: 0, bottom: 0, opacity: rightFade }}>
              {undocked ? (
                <div
                  style={{
                    position: "absolute",
                    inset: 0,
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "center",
                    justifyContent: "center",
                    gap: 10,
                    color: C.subtle,
                  }}
                >
                  <div style={{ fontSize: 17, color: C.text }}>窗口已还原为独立窗口</div>
                  <div style={{ fontSize: 14 }}>点击上方「内嵌窗口」可重新吸附到此处</div>
                </div>
              ) : (
                <div style={{ position: "absolute", inset: 0, background: "#FFFFFF" }}>
                  <DeepSeekPage
                    userMsg={sent ? typedText : undefined}
                    reply={replyVisible ? "好的，正在执行安装并启动 Studio。" : undefined}
                    active
                  />
                </div>
              )}
            </div>
          </div>
        </div>
        <StatusBar left={statusLeft} chat={1} tools={psCount > 0 ? 1 : 0} />
      </div>

      {/* 还原后的独立窗口示意 */}
      {undocked ? (
        <div
          style={{
            position: "absolute",
            left: 1440,
            top: 720,
            width: 420,
            height: 300,
            borderRadius: 10,
            background: "#F7F9FC",
            border: `1px solid ${C.border}`,
            boxShadow: "0 24px 60px rgba(0,0,0,0.55)",
            overflow: "hidden",
            opacity: popIn,
            transform: `scale(${0.94 + popIn * 0.06})`,
          }}
        >
          <div style={{ height: 30, background: "#1F2937", display: "flex", alignItems: "center", padding: "0 10px", gap: 8 }}>
            <svg width={13} height={13} viewBox="0 0 24 24" fill="none" stroke="#93A4B8" strokeWidth={1.8}>
              <circle cx="12" cy="12" r="9" />
              <path d="M3 12h18M12 3c3.2 3.6 3.2 14.4 0 18M12 3c-3.2 3.6-3.2 14.4 0 18" />
            </svg>
            <span style={{ fontSize: 11, color: "#93A4B8", fontFamily: MONO }}>chat.deepseek.com</span>
            <div style={{ marginLeft: "auto", display: "flex", gap: 10, fontSize: 11, color: "#93A4B8" }}>
              {["—", "▢", "✕"].map((g) => (
                <span key={g} style={{ width: 16, textAlign: "center" }}>
                  {g}
                </span>
              ))}
            </div>
          </div>
          <div style={{ padding: 16, fontFamily: FONT, color: "#1C2026", fontSize: 16 }}>DeepSeek</div>
        </div>
      ) : null}

      <CursorArrow x={cx} y={cy} click={clickInput || clickSend || clickUndock || clickDock} />
    </AbsoluteFill>
  );
};
