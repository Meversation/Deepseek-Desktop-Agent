import React from "react";
import {
  AbsoluteFill,
  Easing,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { C, FONT, MONO } from "./theme";
import { useBeatPulse } from "./beat";
import { KineticText } from "./kinetic";

/* ---------------- 通用组件 ---------------- */

export const Fade: React.FC<{ duration: number; children: React.ReactNode }> = ({
  duration,
  children,
}) => {
  const frame = useCurrentFrame();
  const opacity = Math.min(
    interpolate(frame, [0, 4], [0, 1], {
      extrapolateRight: "clamp",
      easing: Easing.out(Easing.cubic),
    }),
    interpolate(frame, [duration - 6, duration], [1, 0], {
      extrapolateLeft: "clamp",
    }),
  );
  return <AbsoluteFill style={{ opacity }}>{children}</AbsoluteFill>;
};

const Caption: React.FC<{ text: string; strong?: string }> = ({ text, strong }) => {
  const frame = useCurrentFrame();
  const y = interpolate(frame, [5, 14], [16, 0], {
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const opacity = interpolate(frame, [5, 14], [0, 1], { extrapolateRight: "clamp" });
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        right: 0,
        bottom: 66,
        display: "flex",
        justifyContent: "center",
        opacity,
        transform: `translateY(${y}px)`,
        fontFamily: FONT,
      }}
    >
      <div
        style={{
          padding: "14px 28px",
          borderRadius: 999,
          background: "rgba(26,32,43,0.85)",
          border: `1px solid ${C.border}`,
          color: C.subtle,
          fontSize: 26,
        }}
      >
        {strong ? <span style={{ color: C.accent, fontWeight: 700 }}>{strong}</span> : null}
        {strong ? "  " : ""}
        <span style={{ color: C.text }}>{text}</span>
      </div>
    </div>
  );
};

const Logo: React.FC<{ size?: number; scale?: number }> = ({ size = 132, scale = 1 }) => {
  const pulse = useBeatPulse(5);
  return (
    <div
      style={{
        width: size,
        height: size,
        transform: `scale(${scale * (1 + pulse * 0.024)})`,
        filter: `drop-shadow(0 0 ${46 + pulse * 26}px rgba(96,165,250,${0.4 + pulse * 0.3}))`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      <Img
        src={staticFile("icon.png")}
        style={{ width: size, height: size, borderRadius: size * 0.22, objectFit: "contain" }}
      />
    </div>
  );
};

/* ---------------- 场景 1：标题 ---------------- */

export const TitleScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const pop = spring({ frame, fps, config: { damping: 200 }, durationInFrames: 22 });
  const lineW = interpolate(frame, [18, 40], [0, 560], {
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const pillOpacity = interpolate(frame, [60, 72], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 34 }}>
        <div style={{ opacity: pop }}>
          <Logo size={124} scale={interpolate(pop, [0, 1], [0.6, 1])} />
        </div>
        <KineticText
          text="Deepseek-Desktop-Agent"
          delay={15}
          stagger={0.9}
          style={{ fontSize: 92, fontWeight: 800, letterSpacing: -2, color: C.text, lineHeight: 1.05 }}
        />
        <div
          style={{
            width: lineW,
            height: 3,
            borderRadius: 2,
            background: `linear-gradient(90deg, ${C.accent2}, ${C.accent})`,
            opacity: 0.9,
          }}
        />
        <KineticText
          text="把网页端 DeepSeek 变成可执行的本地 Agent"
          delay={30}
          stagger={0.7}
          accentFrom={20}
          style={{ fontSize: 34, color: C.text }}
        />
        <div
          style={{
            marginTop: 8,
            padding: "10px 24px",
            borderRadius: 999,
            border: `1px solid ${C.border}`,
            color: C.subtle,
            fontSize: 22,
            letterSpacing: 1.5,
            opacity: pillOpacity,
            transform: `translateY(${(1 - pillOpacity) * 14}px)`,
          }}
        >
          0 成本 · 无需 API Key · 工具在本机执行 · .NET 10 / WinForms / Edge CDP
        </div>
      </div>
    </AbsoluteFill>
  );
};

/* ---------------- 场景 2：核心价值 ---------------- */

const OrbitLabel: React.FC<{
  angle: number;
  radius: number;
  rotate: number;
  text: string;
  delay: number;
}> = ({ angle, radius, rotate, text, delay }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({ frame: Math.max(0, frame - delay), fps, config: { damping: 200 }, durationInFrames: 18 });
  const rad = ((angle + rotate) * Math.PI) / 180;
  return (
    <div
      style={{
        position: "absolute",
        left: "50%",
        top: "50%",
        transform: `translate(-50%,-50%) translate(${Math.cos(rad) * radius}px, ${Math.sin(rad) * radius}px)`,
        opacity: p,
      }}
    >
      <div
        style={{
          padding: "12px 22px",
          borderRadius: 999,
          background: "rgba(26,32,43,0.92)",
          border: `1px solid ${C.border}`,
          fontSize: 21,
          color: C.text,
          whiteSpace: "nowrap",
        }}
      >
        {text}
      </div>
    </div>
  );
};

export const ValueScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const beat = useBeatPulse(3.6);
  const pop = spring({ frame: Math.max(0, frame - 6), fps, config: { damping: 200 }, durationInFrames: 25 });
  const rotate = frame * 0.26;
  const ringOpacity = interpolate(frame, [7, 28], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <div style={{ position: "relative", width: 1000, height: 760 }}>
        <div
          style={{
            position: "absolute",
            left: "50%",
            top: "50%",
            width: 700,
            height: 700,
            marginLeft: -350,
            marginTop: -350,
            borderRadius: "50%",
            border: `1px dashed rgba(96,165,250,${0.2 + beat * 0.35})`,
            opacity: ringOpacity,
            transform: `rotate(${rotate}deg) scale(${1 + beat * 0.018})`,
          }}
        />
        <OrbitLabel angle={-90} radius={330} rotate={rotate} text="无 API Key" delay={15} />
        <OrbitLabel angle={0} radius={330} rotate={rotate} text="无按 Token 计费" delay={23} />
        <OrbitLabel angle={90} radius={330} rotate={rotate} text="PowerShell 在本机执行" delay={31} />
        <OrbitLabel angle={180} radius={330} rotate={rotate} text="网页即模型" delay={39} />

        <div
          style={{
            position: "absolute",
            left: "50%",
            top: "50%",
            transform: `translate(-50%,-50%) scale(${interpolate(pop, [0, 1], [0.72, 1]) * (1 + beat * 0.02)})`,
            opacity: pop,
            textAlign: "center",
          }}
        >
          <div
            style={{
              fontSize: 240,
              fontWeight: 800,
              lineHeight: 1,
              color: C.accent,
              textShadow: `0 0 ${70 + beat * 50}px rgba(96,165,250,${0.35 + beat * 0.3})`,
            }}
          >
            ¥0
          </div>
          <div style={{ marginTop: 6, fontSize: 34, fontWeight: 700, color: C.text }}>每次调用的成本</div>
          <div style={{ marginTop: 14, fontSize: 22, color: C.subtle }}>复用现有网页会话，把执行权交给本机</div>
        </div>
      </div>
      <Caption strong="前提" text="不接付费接口、不买模型额度，能力来自真实网页版 DeepSeek" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 3：内嵌（左输入 / 右网页） ---------------- */

const StepChip: React.FC<{ n: string; text: string; active: boolean }> = ({ n, text, active }) => (
  <div
    style={{
      display: "flex",
      alignItems: "center",
      gap: 10,
      padding: "9px 18px",
      borderRadius: 999,
      border: `1px solid ${active ? C.accent : C.border}`,
      background: active ? "rgba(96,165,250,0.14)" : "transparent",
      color: active ? C.text : C.subtle,
      fontSize: 19,
      fontFamily: FONT,
      opacity: active ? 1 : 0.6,
    }}
  >
    <div
      style={{
        width: 22,
        height: 22,
        borderRadius: 11,
        background: active ? C.accent : C.border,
        color: C.bg,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 14,
        fontWeight: 700,
      }}
    >
      {n}
    </div>
    {text}
  </div>
);

const UiButton: React.FC<{ text: string; accent?: boolean }> = ({ text, accent }) => (
  <div
    style={{
      padding: "10px 22px",
      borderRadius: 8,
      background: accent ? C.accent : C.surface,
      color: accent ? C.bg : C.text,
      border: `1px solid ${accent ? C.accent : C.border}`,
      fontSize: 18,
      fontWeight: 600,
    }}
  >
    {text}
  </div>
);

export const EmbedScene: React.FC = () => {
  const frame = useCurrentFrame();
  const step = frame < 52 ? 1 : frame < 96 ? 2 : 3;
  const pageIn = interpolate(frame, [96, 152], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const loadingOpacity = interpolate(frame, [92, 110], [1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const spinner = (frame % 40) * 9;
  const typed = "让 DeepSeek 帮我装好 Remotion 并启动 Studio";
  const shown = Math.max(0, Math.min(typed.length, Math.floor((frame - 18) / 3)));
  const caretVisible = Math.floor(frame / 16) % 2 === 0;

  return (
    <AbsoluteFill style={{ fontFamily: FONT }}>
      <div
        style={{
          position: "absolute",
          top: 44,
          left: 0,
          right: 0,
          display: "flex",
          justifyContent: "center",
          gap: 16,
        }}
      >
        <StepChip n="1" text="浏览器离屏启动" active={step === 1} />
        <StepChip n="2" text="定位 Edge 窗口" active={step === 2} />
        <StepChip n="3" text="SetParent 嵌入面板" active={step === 3} />
      </div>

      <div
        style={{
          position: "absolute",
          left: 180,
          top: 104,
          width: 1560,
          height: 810,
          borderRadius: 18,
          background: C.bg2,
          border: `1px solid ${C.border}`,
          boxShadow: "0 40px 120px rgba(0,0,0,0.55)",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            height: 52,
            borderBottom: `1px solid ${C.border}`,
            display: "flex",
            alignItems: "center",
            padding: "0 20px",
            gap: 12,
          }}
        >
          <div style={{ width: 12, height: 12, borderRadius: 6, background: "#FF5F57" }} />
          <div style={{ width: 12, height: 12, borderRadius: 6, background: "#FEBC2E" }} />
          <div style={{ width: 12, height: 12, borderRadius: 6, background: "#28C840" }} />
          <div style={{ marginLeft: 14, color: C.subtle, fontSize: 21 }}>Deepseek-Desktop-Agent</div>
          <div style={{ marginLeft: "auto", color: C.subtle, fontSize: 17, fontFamily: MONO }}>
            状态：{step === 3 ? "已内嵌" : "启动中"}
          </div>
        </div>

        <div style={{ position: "absolute", top: 52, left: 0, right: 0, bottom: 0, display: "flex" }}>
          {/* 左：用户输入与控制 */}
          <div
            style={{
              width: 560,
              borderRight: `1px solid ${C.border}`,
              padding: 28,
              display: "flex",
              flexDirection: "column",
              gap: 14,
            }}
          >
            <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between" }}>
              <span style={{ fontSize: 25, fontWeight: 700, color: C.text }}>对话</span>
              <span style={{ fontSize: 16, color: C.subtle, fontFamily: MONO }}>Works\remotion-intro</span>
            </div>
            <div style={{ fontSize: 17, color: C.subtle }}>
              AI 回复 · <span style={{ color: C.accent }}>Markdown 渲染</span>
            </div>
            <div
              style={{
                padding: "16px 18px",
                borderRadius: 12,
                background: C.surface,
                border: `1px solid ${C.border}`,
                fontSize: 19,
                color: C.text,
                lineHeight: 1.6,
              }}
            >
              <div>依赖安装完成，Studio 已在 3000 端口启动。</div>
              <div style={{ marginTop: 8, color: C.subtle, fontSize: 17 }}>• Remotion 4.0 · React 19 · TypeScript</div>
            </div>
            <div style={{ flex: 1 }} />
            <div
              style={{
                position: "relative",
                height: 128,
                borderRadius: 12,
                background: C.input,
                border: `1px solid ${C.border}`,
                padding: 16,
                fontSize: 19,
                color: C.text,
              }}
            >
              {shown > 0 ? typed.slice(0, shown) : <span style={{ color: C.subtle }}>描述要完成的任务…</span>}
              <span style={{ color: C.accent, opacity: caretVisible ? 1 : 0 }}>|</span>
              <div style={{ position: "absolute", left: 16, bottom: 12, fontSize: 15, color: C.subtle }}>
                Enter 发送 · Shift+Enter 换行
              </div>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <UiButton text="发送" accent />
              <UiButton text="停止" />
              <div style={{ marginLeft: "auto", fontSize: 16, color: C.subtle }}>输入框 · 用户操作区</div>
            </div>
          </div>

          {/* 右：内嵌的 DeepSeek 网页 */}
          <div style={{ flex: 1, padding: 18, display: "flex", flexDirection: "column", gap: 12 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div
                style={{
                  width: 8,
                  height: 8,
                  borderRadius: 4,
                  background: pageIn > 0.6 ? C.ok : C.warn,
                }}
              />
              <span style={{ fontSize: 18, color: C.subtle, fontFamily: MONO }}>chat.deepseek.com</span>
              <span
                style={{
                  marginLeft: "auto",
                  fontSize: 15,
                  padding: "4px 12px",
                  borderRadius: 999,
                  border: `1px solid ${C.border}`,
                  color: pageIn > 0.6 ? C.ok : C.subtle,
                }}
              >
                {pageIn > 0.6 ? "已内嵌" : "等待加载"}
              </span>
            </div>
            <div
              style={{
                position: "relative",
                flex: 1,
                borderRadius: 12,
                border: `1px dashed ${pageIn > 0.6 ? C.border : "rgba(96,165,250,0.45)"}`,
                overflow: "hidden",
                background: C.surface,
              }}
            >
              <div
                style={{
                  position: "absolute",
                  inset: 0,
                  display: "flex",
                  flexDirection: "column",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: 18,
                  opacity: loadingOpacity,
                }}
              >
                <div
                  style={{
                    width: 46,
                    height: 46,
                    borderRadius: 23,
                    border: "3px solid rgba(96,165,250,0.2)",
                    borderTopColor: C.accent,
                    transform: `rotate(${spinner}deg)`,
                  }}
                />
                <div style={{ fontSize: 18, color: C.subtle }}>正在加载网页…</div>
              </div>

              <div
                style={{
                  position: "absolute",
                  inset: 0,
                  background: "#F7F9FC",
                  opacity: pageIn,
                  transform: `scale(${interpolate(pageIn, [0, 1], [0.96, 1])})`,
                }}
              >
                <div style={{ padding: 34, fontFamily: FONT, color: "#1C2026" }}>
                  <div style={{ fontSize: 28, fontWeight: 700 }}>DeepSeek</div>
                  <div style={{ marginTop: 10, fontSize: 20, color: "#626A76" }}>今天想探索什么？</div>
                  <div
                    style={{
                      marginTop: 24,
                      height: 132,
                      borderRadius: 12,
                      border: "1px solid #DADFE6",
                      background: "#FFFFFF",
                    }}
                  />
                  <div style={{ marginTop: 18, display: "flex", gap: 12 }}>
                    {["安装依赖", "生成动画", "分析报错"].map((t) => (
                      <div
                        key={t}
                        style={{
                          padding: "9px 16px",
                          borderRadius: 999,
                          border: "1px solid #DADFE6",
                          fontSize: 17,
                          color: "#626A76",
                        }}
                      >
                        {t}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <Caption strong="布局" text="左侧输入与状态，右侧是真实的 DeepSeek 网页；加载完成后吸附进右侧容器" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 4：实现流程（对齐真实代码） ---------------- */

const ImplStep: React.FC<{
  index: number;
  file: string;
  desc: string;
  delay: number;
  accent?: boolean;
}> = ({ index, file, desc, delay, accent }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const beat = useBeatPulse(4);
  const p = spring({ frame: Math.max(0, frame - delay), fps, config: { damping: 200 }, durationInFrames: 26 });
  return (
    <div
      style={{
        width: 300,
        minHeight: 206,
        padding: "20px 18px",
        borderRadius: 14,
        background: accent ? `rgba(77,107,254,${0.14 + beat * 0.08})` : C.surface,
        border: `1px solid ${accent ? `rgba(96,165,250,${0.6 + beat * 0.3})` : C.border}`,
        boxShadow: accent ? `0 0 ${18 + beat * 26}px rgba(96,165,250,${0.12 + beat * 0.18})` : "none",
        transform: `translateY(${(1 - p) * 24}px)`,
        opacity: p,
        display: "flex",
        flexDirection: "column",
        gap: 12,
        fontFamily: FONT,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <div
          style={{
            width: 26,
            height: 26,
            borderRadius: 13,
            background: accent ? C.accent : C.border,
            color: C.bg,
            fontSize: 14,
            fontWeight: 800,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            flexShrink: 0,
          }}
        >
          {index}
        </div>
        <div style={{ fontFamily: MONO, fontSize: 17, color: accent ? C.accent : C.text }}>{file}</div>
      </div>
      <div style={{ fontSize: 16, color: C.subtle, lineHeight: 1.65 }}>{desc}</div>
    </div>
  );
};

const ImplArrow: React.FC<{ delay: number }> = ({ delay }) => {
  const frame = useCurrentFrame();
  const p = interpolate(frame, [delay, delay + 16], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return (
    <div style={{ width: 30, display: "flex", alignItems: "center", justifyContent: "center", opacity: p }}>
      <svg width={24} height={12} viewBox="0 0 24 12">
        <line x1={0} y1={6} x2={16} y2={6} stroke={C.accent} strokeWidth={1.6} />
        <polygon points="16,2 24,6 16,10" fill={C.accent} />
      </svg>
    </div>
  );
};

export const ArchitectureScene: React.FC = () => {
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <KineticText
        text="实现流程：从启动到回传"
        stagger={0.8}
        style={{ fontSize: 46, fontWeight: 800, color: C.text }}
      />
      <div style={{ marginTop: 14, marginBottom: 38, fontSize: 21, color: C.subtle }}>
        全部在本机进程内完成 · 没有服务端 · 没有 API Key
      </div>
      <div style={{ display: "flex", alignItems: "stretch" }}>
        <ImplStep index={1} file="Program.cs" desc="单实例 Mutex，禁止重复拉起；全局异常写入崩溃日志" delay={6} />
        <ImplArrow delay={18} />
        <ImplStep
          index={2}
          file="BrowserManager.cs"
          desc="离屏启动 Edge（--remote-debugging-port=9222），定位窗口后 SetParent 嵌入"
          delay={16}
          accent
        />
        <ImplArrow delay={28} />
        <ImplStep
          index={3}
          file="CdpClient.cs"
          desc="WebSocket 连 CDP：Runtime.evaluate 注入脚本，Input 事件发送消息"
          delay={26}
        />
        <ImplArrow delay={38} />
        <ImplStep
          index={4}
          file="ToolExecutor.cs"
          desc="解析回复中的工具调用 → PowerShellRunner 执行 → stdout 逐行回传"
          delay={36}
          accent
        />
        <ImplArrow delay={48} />
        <ImplStep
          index={5}
          file="MarkdownRenderer.cs"
          desc="DOM→Markdown→RTF 两级转换，把回复渲染回 WinForms 对话区"
          delay={46}
        />
      </div>
      <Caption strong="边界" text="能力来自真实网页会话，因此实现必须贴着页面结构走，而不是调用官方接口" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 5：工具闭环（环形轨道 + 实时终端） ---------------- */

const OrbitNodeMini: React.FC<{ x: number; y: number; text: string; glow: number }> = ({
  x,
  y,
  text,
  glow,
}) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      transform: `translate(-50%,-50%) scale(${1 + glow * 0.035})`,
      padding: "13px 20px",
      borderRadius: 12,
      background: `rgba(96,165,250,${0.04 + glow * 0.18})`,
      border: `1px solid rgba(96,165,250,${0.16 + glow * 0.64})`,
      color: `rgba(230,235,242,${0.58 + glow * 0.42})`,
      fontSize: 21,
      whiteSpace: "nowrap",
      fontFamily: FONT,
      boxShadow: `0 0 ${Math.round(34 * glow)}px rgba(96,165,250,${0.3 * glow})`,
    }}
  >
    {text}
  </div>
);

const AppBadge: React.FC<{ size: number }> = ({ size }) => (
  <Img
    src={staticFile("icon.png")}
    style={{ width: size, height: size, borderRadius: size * 0.22, objectFit: "contain" }}
  />
);

const WinControls: React.FC = () => (
  <div style={{ marginLeft: "auto", display: "flex", gap: 4, color: C.subtle, fontSize: 14 }}>
    {["—", "▢", "✕"].map((g) => (
      <span key={g} style={{ width: 22, textAlign: "center" }}>
        {g}
      </span>
    ))}
  </div>
);

const TERMINAL_LINES = [
  { at: 10, text: "tool_call → shell · 1 条命令", color: C.accent },
  { at: 28, text: "PS> dotnet build .\\src\\DeepseekDesktopAgent -c Release", color: C.text },
  { at: 50, text: "Build succeeded · 0 Warning(s) · 0 Error(s)", color: C.subtle },
  { at: 72, text: "→ stdout 已回传对话，等待下一步指令", color: C.ok },
];

const LiveTerminal: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <div
      style={{
        width: 700,
        borderRadius: 14,
        border: `1px solid ${C.border}`,
        background: "#0E131B",
        overflow: "hidden",
        fontFamily: MONO,
      }}
    >
      <div
        style={{
          height: 42,
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "0 14px",
          borderBottom: `1px solid ${C.border}`,
        }}
      >
        <AppBadge size={16} />
        <span style={{ fontSize: 15, color: C.subtle }}>PowerShell · 实时输出</span>
        <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 7,
              height: 7,
              borderRadius: 4,
              background: C.ok,
              opacity: 0.45 + 0.55 * (0.5 + 0.5 * Math.sin(frame / 7)),
            }}
          />
          <span style={{ fontSize: 13, color: C.ok }}>运行中</span>
        </div>
      </div>
      <div style={{ padding: "20px 22px", fontSize: 17, lineHeight: 2, minHeight: 252 }}>
        {TERMINAL_LINES.map((l) => {
          if (frame < l.at) {
            return null;
          }
          const chars = Math.min(l.text.length, Math.floor((frame - l.at) * 3.4) + 1);
          return (
            <div key={l.text} style={{ color: l.color }}>
              {l.text.slice(0, chars)}
              {chars < l.text.length ? <span style={{ color: C.accent }}>▌</span> : null}
            </div>
          );
        })}
      </div>
    </div>
  );
};

const FLOW_W = 118;

const FlowArrow: React.FC<{ dir: 1 | -1; color: string; label: string; progress: number }> = ({
  dir,
  color,
  label,
  progress,
}) => {
  const start = dir === 1 ? 0 : FLOW_W;
  const end = dir === 1 ? FLOW_W : 0;
  const cx = start + (end - start) * progress;
  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 10 }}>
      <div style={{ fontSize: 15, color: C.text, fontFamily: MONO, letterSpacing: 1 }}>{label}</div>
      <svg width={FLOW_W} height={16} viewBox={`0 0 ${FLOW_W} 16`} style={{ display: "block" }}>
        <line
          x1={dir === 1 ? 2 : FLOW_W - 2}
          y1={8}
          x2={dir === 1 ? FLOW_W - 12 : 12}
          y2={8}
          stroke={color}
          strokeWidth={1.6}
          strokeDasharray="5 5"
          opacity={0.5}
        />
        <polygon
          points={dir === 1 ? `${FLOW_W - 12},2 ${FLOW_W},8 ${FLOW_W - 12},14` : `12,2 0,8 12,14`}
          fill={color}
          opacity={0.9}
        />
        <circle cx={cx} cy={8} r={4} fill={color} />
      </svg>
    </div>
  );
};

export const LoopScene: React.FC = () => {
  const frame = useCurrentFrame();
  const stageStarts = [4, 32, 60, 88];
  const cycle = 112;
  const rotate = frame * (360 / cycle) - 90;
  const rad = (rotate * Math.PI) / 180;
  const radius = 200;
  const nodes = ["AI 回复携带工具调用", "解析工具协议", "本机执行", "结果回传"];
  const positions = [
    [400, 100],
    [600, 310],
    [400, 520],
    [200, 310],
  ];
  const glowAt = (i: number) => {
    const start = stageStarts[i];
    const end = i < stageStarts.length - 1 ? stageStarts[i + 1] : cycle;
    return interpolate(frame, [start - 10, start + 6, end - 6, end + 10], [0, 1, 1, 0], {
      extrapolateLeft: "clamp",
      extrapolateRight: "clamp",
    });
  };
  return (
    <AbsoluteFill
      style={{
        alignItems: "center",
        justifyContent: "center",
        flexDirection: "column",
        fontFamily: FONT,
      }}
    >
      <KineticText
        text="工具调用形成闭环"
        stagger={1}
        style={{ fontSize: 44, fontWeight: 800, color: C.text, marginBottom: 18 }}
      />
      <div style={{ display: "flex", alignItems: "center", gap: 0 }}>
        <div style={{ position: "relative", width: 800, height: 620 }}>
          <div
            style={{
              position: "absolute",
              left: 400 - radius,
              top: 310 - radius,
              width: radius * 2,
              height: radius * 2,
              borderRadius: "50%",
              border: `1px solid rgba(96,165,250,0.22)`,
            }}
          />
          {nodes.map((n, i) => (
            <OrbitNodeMini key={n} x={positions[i][0]} y={positions[i][1]} text={n} glow={glowAt(i)} />
          ))}
          <div
            style={{
              position: "absolute",
              left: 400 + Math.cos(rad) * radius - 8,
              top: 310 + Math.sin(rad) * radius - 8,
              width: 16,
              height: 16,
              borderRadius: 8,
              background: C.accent,
              boxShadow: `0 0 22px ${C.accent}`,
            }}
          />
          <div
            style={{
              position: "absolute",
              left: 300,
              top: 292,
              width: 200,
              textAlign: "center",
              color: C.subtle,
              fontSize: 18,
              letterSpacing: 2,
            }}
          >
            消息 · 执行 · 回传
          </div>
          {/* 环形轨道到右侧终端的因果引线，与外部箭头同高 */}
          <svg
            width={800}
            height={620}
            style={{ position: "absolute", left: 0, top: 0, pointerEvents: "none" }}
          >
            <line
              x1={566}
              y1={213}
              x2={800}
              y2={213}
              stroke={C.accent}
              strokeWidth={1.6}
              strokeDasharray="5 5"
              opacity={0.5}
            />
            <line
              x1={800}
              y1={435}
              x2={566}
              y2={435}
              stroke={C.ok}
              strokeWidth={1.6}
              strokeDasharray="5 5"
              opacity={0.5}
            />
          </svg>
        </div>
        <div
          style={{
            width: FLOW_W,
            height: 620,
            display: "flex",
            flexDirection: "column",
            justifyContent: "space-evenly",
            alignItems: "center",
          }}
        >
          <FlowArrow dir={1} color={C.accent} label="tool call" progress={(frame % 30) / 30} />
          <FlowArrow dir={-1} color={C.ok} label="result" progress={((frame + 15) % 30) / 30} />
        </div>
        <LiveTerminal />
      </div>
      <Caption strong="可观测" text="PowerShell 输出逐行回传界面，执行过程可观察、可中断" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 6：稳定性（竖向时间线 + 检查清单） ---------------- */

const CheckRow: React.FC<{ index: number; title: string; desc: string }> = ({ index, title, desc }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const start = 14 + index * 20;
  const p = interpolate(frame, [start, start + 14], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const check = spring({
    frame: Math.max(0, frame - start - 3),
    fps,
    config: { damping: 16, stiffness: 200, mass: 0.55 },
    durationInFrames: 12,
  });
  return (
    <div
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: 22,
        opacity: p,
        transform: `translateX(${(1 - p) * 22}px)`,
        paddingBottom: 22,
      }}
    >
      <div style={{ position: "relative", width: 40, flexShrink: 0 }}>
        <div
          style={{
            width: 28,
            height: 28,
            marginLeft: 6,
            marginTop: 3,
            borderRadius: 14,
            background: check > 0.05 ? C.ok : C.border,
            color: C.bg,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 15,
            fontWeight: 800,
            transform: `scale(${Math.max(0.001, check)})`,
          }}
        >
          ✓
        </div>
      </div>
      <div>
        <div style={{ fontSize: 27, fontWeight: 700, color: C.text }}>{title}</div>
        <div style={{ marginTop: 6, fontSize: 19, color: C.subtle, lineHeight: 1.5 }}>{desc}</div>
      </div>
    </div>
  );
};

export const StabilityScene: React.FC = () => {
  const frame = useCurrentFrame();
  const items = [
    { title: "单实例启动", desc: "互斥门闩串行化启动流程，避免并发拉起多个浏览器实例" },
    { title: "CDP 超时与重连", desc: "命令级超时、连接复用与重试，异常显式上报而非静默等待" },
    { title: "启动不闪窗", desc: "浏览器离屏加载，定位后立即隐藏，内嵌完成再显示" },
    { title: "会话续接", desc: "持久化会话 id 与地址，重开直接恢复上下文" },
    { title: "Markdown 渲染", desc: "DOM→Markdown→RTF 两级转换，保留标题、列表、代码块与表格" },
    { title: "执行期输入锁定", desc: "工具运行期间禁用输入并提示原因，避免手工操作干扰协议" },
  ];
  const passed = items.filter((_, i) => frame >= 20 + i * 20).length;
  const badge = interpolate(frame, [126, 142], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <div style={{ width: 1280 }}>
        <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", marginBottom: 32 }}>
          <div>
            <KineticText
              text="稳定性：从设计到验证"
              stagger={0.9}
              style={{ fontSize: 44, fontWeight: 800, color: C.text, justifyContent: "flex-start" }}
            />
            <div style={{ marginTop: 10, fontSize: 21, color: C.subtle }}>
              四类链路上的关键改动，逐项经实机验证
            </div>
          </div>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 12,
              padding: "10px 22px",
              borderRadius: 999,
              background: "rgba(52,211,153,0.12)",
              border: `1px solid rgba(52,211,153,0.5)`,
              color: C.ok,
              fontSize: 20,
              opacity: badge,
              transform: `scale(${interpolate(badge, [0, 1], [0.9, 1])})`,
            }}
          >
            ✓ selftest 15 / 15 PASS
          </div>
        </div>
        <div style={{ position: "relative" }}>
          <div
            style={{
              position: "absolute",
              left: 18,
              top: 14,
              bottom: 34,
              width: 2,
              background: `linear-gradient(180deg, rgba(96,165,250,0.55), rgba(96,165,250,0.06))`,
            }}
          />
          {items.map((it, i) => (
            <CheckRow key={it.title} index={i} title={it.title} desc={it.desc} />
          ))}
        </div>
        <div style={{ marginTop: 4, marginLeft: 48, fontSize: 18, color: C.subtle }}>
          已完成 {passed} / {items.length} 项
        </div>
      </div>
      <Caption strong="原则" text="每个失败的环节都要能定位原因，而不是靠重试掩盖" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景：风险与边界 ---------------- */

const RISK_ITEMS = [
  {
    title: "账号与条款",
    risk: "自动化操作网页端，可能触发平台风控，或与 DeepSeek 服务条款冲突",
    fix: "定位为个人自用、低频调用，不共享账号、不做批量分发",
  },
  {
    title: "页面结构耦合",
    risk: "依赖 chat.deepseek.com 的 DOM 与选择器，前端改版会让发送或读取失败",
    fix: "选择器集中封装，失败显式报错并提示，而不是静默等待",
  },
  {
    title: "CDP 与 Edge 版本",
    risk: "9222 调试端口可能被占用，Edge 升级后 CDP 行为也可能变化",
    fix: "看门狗检测掉线并重建浏览器，异常统一写入崩溃日志",
  },
  {
    title: "会话有效性",
    risk: "新会话的临时 id 未落盘，重开时会提示「该对话不存在」",
    fix: "只持久化侧边栏已确认的会话地址，失效时回退到会话选择",
  },
  {
    title: "本机执行风险",
    risk: "PowerShell 命令真实执行，可能误删或误改工作目录内的文件",
    fix: "默认人工确认；「自动执行」需显式勾选；执行期间锁定输入",
  },
  {
    title: "单实例与临时目录",
    risk: "多开会让多个浏览器并发拉起；崩溃重连会清空临时目录",
    fix: "Mutex 单实例；重连后重新提交系统提示词并告知用户",
  },
];

const RiskCard: React.FC<{ index: number; title: string; risk: string; fix: string }> = ({
  index,
  title,
  risk,
  fix,
}) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({
    frame: Math.max(0, frame - 10 - index * 10),
    fps,
    config: { damping: 200 },
    durationInFrames: 24,
  });
  return (
    <div
      style={{
        width: 740,
        padding: "18px 22px",
        borderRadius: 14,
        background: C.surface,
        border: `1px solid ${C.border}`,
        borderLeft: `4px solid ${C.warn}`,
        opacity: p,
        transform: `translateY(${(1 - p) * 18}px)`,
        display: "flex",
        flexDirection: "column",
        gap: 8,
        fontFamily: FONT,
      }}
    >
      <div style={{ fontSize: 23, fontWeight: 700, color: C.text }}>{title}</div>
      <div style={{ fontSize: 17, color: C.subtle, lineHeight: 1.55 }}>{risk}</div>
      <div style={{ fontSize: 16, color: C.ok, lineHeight: 1.5 }}>缓解 · {fix}</div>
    </div>
  );
};

export const RiskScene: React.FC = () => {
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <KineticText text="风险与边界" stagger={1} style={{ fontSize: 46, fontWeight: 800, color: C.text }} />
      <div style={{ marginTop: 12, marginBottom: 30, fontSize: 21, color: C.subtle }}>
        收益是真实的，代价也要写在明面上
      </div>
      <div style={{ display: "flex", flexWrap: "wrap", gap: 20, width: 1500, justifyContent: "center" }}>
        {RISK_ITEMS.map((r, i) => (
          <RiskCard key={r.title} index={i} title={r.title} risk={r.risk} fix={r.fix} />
        ))}
      </div>
      <Caption strong="原则" text="不隐藏依赖：网页结构、账号状态与本地权限，都是这份方案的一部分" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 7：发布（按体积成比例的对比条） ---------------- */

const SizeBar: React.FC<{
  name: string;
  size: string;
  ratio: number;
  note: string;
  delay: number;
  accent?: boolean;
}> = ({ name, size, ratio, note, delay, accent }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({ frame: Math.max(0, frame - delay), fps, config: { damping: 200 }, durationInFrames: 36 });
  return (
    <div style={{ opacity: Math.min(1, p * 2), transform: `translateY(${(1 - p) * 26}px)` }}>
      <div style={{ display: "flex", alignItems: "baseline", gap: 16, marginBottom: 10 }}>
        <div style={{ fontSize: 20, color: C.text, fontFamily: MONO }}>{name}</div>
        <div
          style={{
            marginLeft: "auto",
            fontSize: 40,
            fontWeight: 800,
            color: accent ? C.accent : C.text,
            fontFamily: FONT,
          }}
        >
          {size}
        </div>
      </div>
      <div
        style={{
          height: 34,
          borderRadius: 8,
          background: C.surface,
          border: `1px solid ${C.border}`,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            width: `${interpolate(p, [0, 1], [0, ratio * 100])}%`,
            height: "100%",
            background: accent ? `linear-gradient(90deg, ${C.accent2}, ${C.accent})` : "rgba(150,160,175,0.55)",
          }}
        />
      </div>
      <div style={{ marginTop: 8, fontSize: 17, color: C.subtle, fontFamily: FONT }}>{note}</div>
    </div>
  );
};

export const ReleaseScene: React.FC = () => {
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <div style={{ width: 1240 }}>
        <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", marginBottom: 48 }}>
          <KineticText
            text="单文件发布，解压即用"
            stagger={1}
            style={{ fontSize: 44, fontWeight: 800, color: C.text, justifyContent: "flex-start" }}
          />
          <div style={{ fontSize: 19, color: C.subtle }}>两个产物均通过 --selftest</div>
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 42 }}>
          <SizeBar
            name="Deepseek-Desktop-Agent-self-contained.exe"
            size="104 MB"
            ratio={1}
            note="内置 .NET 运行时，拷贝到任意 Win x64 机器即可运行"
            delay={10}
            accent
          />
          <SizeBar
            name="Deepseek-Desktop-Agent-framework-dependent.exe"
            size="1.7 MB"
            ratio={1.7 / 104}
            note="约为自包含版的 1 / 61，需预装 .NET 10 桌面运行时"
            delay={34}
          />
        </div>
      </div>
      <Caption strong="交付" text="不需要安装器，也不需要额外配置；打开后在左侧点「开始」即进入内嵌会话" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 8：使用流程 ---------------- */

export const FlowScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const steps = [
    { n: "1", t: "选择工作目录", d: "路径写入配置；下次启动会询问是否继续该目录" },
    { n: "2", t: "选新对话 / 旧对话", d: "旧对话自动重开上次会话，失效则弹出会话选择" },
    { n: "3", t: "点「开始 / 继续」", d: "启动 Edge（9222）、内嵌右侧、提交 system.txt 系统提示词" },
    { n: "4", t: "输入任务 · 随时停止", d: "停止后可用「切换会话」；窗口可「还原 / 内嵌」" },
  ];
  const xs = [140, 460, 780, 1100];
  const active = Math.min(3, Math.max(0, Math.floor((frame - 6) / 24)));
  const lineW = interpolate(frame, [6, 116], [0, 1080], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const pulseX = interpolate(frame, [6, 116], [20, 1200], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <KineticText
        text="如何开始：与真实界面一一对应"
        stagger={0.7}
        style={{ fontSize: 44, fontWeight: 800, color: C.text, marginBottom: 34 }}
      />
      <div style={{ position: "relative", width: 1240, height: 400 }}>
        <div style={{ position: "absolute", left: 20, top: 198, width: 1200, height: 3, borderRadius: 2, background: C.border }} />
        <div
          style={{
            position: "absolute",
            left: 20,
            top: 197,
            width: lineW,
            height: 5,
            borderRadius: 3,
            background: `linear-gradient(90deg, ${C.accent2}, ${C.accent})`,
          }}
        />
        <div
          style={{
            position: "absolute",
            left: pulseX,
            top: 190,
            width: 16,
            height: 16,
            borderRadius: 8,
            background: "#fff",
            boxShadow: `0 0 26px ${C.accent}`,
          }}
        />
        {steps.map((s, i) => {
          const p = spring({ frame: Math.max(0, frame - 8 - i * 24), fps, config: { damping: 200 }, durationInFrames: 22 });
          const isActive = i === active;
          const passed = i < active;
          const above = i % 2 === 0;
          return (
            <div key={s.n} style={{ position: "absolute", left: xs[i], top: 200 }}>
              <div
                style={{
                  position: "absolute",
                  left: 0,
                  top: 0,
                  width: 58,
                  height: 58,
                  marginLeft: -29,
                  marginTop: -29,
                  borderRadius: 29,
                  background: isActive ? C.accent : passed ? "rgba(96,165,250,0.22)" : C.surface,
                  border: `2px solid ${isActive || passed ? C.accent : C.border}`,
                  color: isActive ? C.bg : C.text,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  fontSize: 23,
                  fontWeight: 800,
                  boxShadow: isActive ? `0 0 40px rgba(96,165,250,0.5)` : "none",
                  transform: `scale(${p * (isActive ? 1.08 : 1)})`,
                  opacity: p,
                }}
              >
                {s.n}
              </div>
              <div
                style={{
                  position: "absolute",
                  left: -140,
                  top: above ? -118 : 62,
                  width: 280,
                  textAlign: "center",
                  opacity: p,
                }}
              >
                <div style={{ fontSize: 27, fontWeight: 700, color: isActive || passed ? C.text : C.subtle }}>{s.t}</div>
                <div style={{ marginTop: 6, fontSize: 18, color: C.subtle, lineHeight: 1.45 }}>{s.d}</div>
              </div>
            </div>
          );
        })}
      </div>
      <Caption strong="对齐代码" text="控件与分支来自 MainForm.cs：StartFlowAsync 与 SwitchSessionAsync" />
    </AbsoluteFill>
  );
};

/* ---------------- 场景 9：结尾 ---------------- */

export const OutroScene: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({ frame: Math.max(0, frame - 2), fps, config: { damping: 200 }, durationInFrames: 26 });
  const lineW = interpolate(frame, [22, 40], [0, 520], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", fontFamily: FONT }}>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 28 }}>
        <Logo size={104} scale={p} />
        <KineticText
          text="Deepseek-Desktop-Agent"
          delay={10}
          stagger={0.8}
          style={{ fontSize: 62, fontWeight: 800, color: C.text }}
        />
        <div style={{ width: lineW, height: 3, background: C.accent, borderRadius: 2 }} />
        <div style={{ fontSize: 26, color: C.text, opacity: interpolate(frame, [26, 38], [0, 1], { extrapolateLeft: "clamp" }) }}>
          网页端的能力，本机的执行权 · 0 成本
        </div>
        <div
          style={{
            fontFamily: MONO,
            fontSize: 23,
            color: C.text,
            background: C.bg2,
            border: `1px solid ${C.border}`,
            borderRadius: 12,
            padding: "18px 30px",
            lineHeight: 1.7,
            opacity: interpolate(frame, [42, 56], [0, 1], { extrapolateLeft: "clamp" }),
            transform: `translateY(${interpolate(frame, [42, 56], [12, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" })}px)`,
          }}
        >
          <div>
            <span style={{ color: C.subtle }}>$ </span>dotnet build ./src/DeepseekDesktopAgent -c Release
          </div>
          <div>
            <span style={{ color: C.subtle }}>$ </span>dotnet publish -r win-x64 --self-contained true
          </div>
        </div>
      </div>
    </AbsoluteFill>
  );
};
