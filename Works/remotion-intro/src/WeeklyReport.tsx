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
 * W44 周报 · 改版 v2 · 实测向
 * 
 * 核心改造：从「功能盘点」→「真实任务演示」
 * - 前3秒强钩子：直接展示任务目标
 * - 每个章节 = 一个可复现的小任务
 * - 强调收益（0成本、非反代、本地文件）
 * - 结尾引导收藏/评论
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
  const end = index < B.length - 1 ? B[index + 1] : TOTAL;
  const local = frame - start;
  const duration = end - start;
  if (duration <= 0) return null;
  if (local < -2 || local > duration + 2) return null;
  const enter = interpolate(local, [0, 5], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const exit = interpolate(local, [Math.max(duration - 4, 0), duration], [1, 0], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const zoom = interpolate(local, [0, duration], [1.035, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  return (
    <AbsoluteFill style={{ opacity: Math.min(enter, exit), transform: `scale(${zoom})` }}>{children}</AbsoluteFill>
  );
};

const Backdrop: React.FC = () => (
  <>
    <AbsoluteFill style={{ background: "#06080D" }} />
    <Img
      src={staticFile("report/app-bg.png")}
      style={{ width: "100%", height: "100%", objectFit: "cover", opacity: 0.4 }}
    />
    <AbsoluteFill
      style={{
        background: "linear-gradient(115deg, transparent 34%, rgba(77,141,255,0.08) 50%, transparent 66%)",
      }}
    />
    <AbsoluteFill style={{ background: "radial-gradient(1000px 700px at 18% 10%, rgba(35,62,120,0.5), transparent 70%)" }} />
    <AbsoluteFill style={{ background: "radial-gradient(900px 620px at 92% 92%, rgba(77,141,255,0.12), transparent 70%)" }} />
    <AbsoluteFill style={{ background: "linear-gradient(180deg, rgba(6,8,13,0.3) 0%, rgba(6,8,13,0.92) 100%)" }} />
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
        color: "rgba(255,255,255,0.03)",
      }}
    >
      TASK
    </div>
  </>
);

/** 任务卡片：展示一个具体任务及其收益 */
const TaskCard: React.FC<{
  local: number;
  step: string;
  title: string;
  subtitle: string;
  benefit: string;
}> = ({ local, step, title, subtitle, benefit }) => (
  <div
    style={{
      padding: "32px 0",
      borderBottom: `1px solid ${C.border}`,
      ...rise(local, 18, 14),
    }}
  >
    <div style={{ display: "flex", alignItems: "center", gap: 16, marginBottom: 8 }}>
      <span
        style={{
          fontFamily: MONO,
          fontSize: 16,
          color: C.accent,
          padding: "4px 12px",
          borderRadius: 6,
          border: `1px solid ${C.accent2}`,
          background: "rgba(77,141,255,0.08)",
        }}
      >
        {step}
      </span>
      <span style={{ fontSize: 36, fontWeight: 700, color: C.text }}>{title}</span>
    </div>
    <div style={{ marginLeft: 0, marginTop: 4, fontSize: 22, color: C.subtle, paddingLeft: 0 }}>{subtitle}</div>
    <div
      style={{
        marginTop: 14,
        marginLeft: 0,
        fontSize: 20,
        color: C.ok,
        fontFamily: MONO,
      }}
    >
      ✓ {benefit}
    </div>
  </div>
);

/** 任务演示场景 */
const TaskDemo: React.FC<{
  local: number;
  task: string;
  input: string;
  result: string;
  delay?: number;
}> = ({ local, task, input, result, delay = 24 }) => {
  const p = interpolate(local, [delay, delay + 40], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  return (
    <div
      style={{
        opacity: p,
        transform: `translateY(${(1 - p) * 20}px)`,
        padding: "28px 0",
        borderBottom: `1px solid ${C.border}`,
      }}
    >
      <div style={{ fontSize: 16, color: C.subtle, marginBottom: 10, fontFamily: MONO }}>
        📋 任务：{task}
      </div>
      <div style={{ display: "flex", gap: 20, alignItems: "stretch" }}>
        <div
          style={{
            flex: 1,
            background: "rgba(255,255,255,0.04)",
            border: `1px solid ${C.border}`,
            borderRadius: 10,
            padding: "16px 20px",
          }}
        >
          <div style={{ fontSize: 14, color: C.subtle, marginBottom: 8, fontFamily: MONO }}>输入</div>
          <div style={{ fontSize: 24, color: C.text }}>{input}</div>
        </div>
        <div style={{ fontSize: 28, color: C.accent, alignSelf: "center" }}>→</div>
        <div
          style={{
            flex: 1,
            background: "rgba(52,211,153,0.06)",
            border: `1px solid rgba(52,211,153,0.3)`,
            borderRadius: 10,
            padding: "16px 20px",
          }}
        >
          <div style={{ fontSize: 14, color: C.ok, marginBottom: 8, fontFamily: MONO }}>结果</div>
          <div style={{ fontSize: 24, color: C.ok }}>{result}</div>
        </div>
      </div>
    </div>
  );
};

const Hero: React.FC = () => {
  const local = useCurrentFrame();
  return (
    <Scene index={0}>
      {/* 强钩子开场：3秒内说清"你要获得什么" */}
      <div
        style={{
          position: "absolute",
          left: 110,
          top: 200,
          width: 780,
        }}
      >
        {/* 标签：痛点关键词 */}
        <div
          style={{
            display: "inline-flex",
            gap: 12,
            marginBottom: 20,
            ...rise(local, 2, 10),
          }}
        >
          {["0成本", "非反代", "本地文件", "免安装"].map((tag, i) => (
            <span
              key={tag}
              style={{
                padding: "6px 14px",
                borderRadius: 6,
                background: "rgba(77,141,255,0.12)",
                border: `1px solid ${C.accent2}`,
                fontFamily: MONO,
                fontSize: 16,
                color: C.accent,
                opacity: interpolate(local, [4 + i * 3, 6 + i * 3], [0, 1], {
                  extrapolateLeft: "clamp",
                  extrapolateRight: "clamp",
                }),
              }}
            >
              {tag}
            </span>
          ))}
        </div>

        {/* 主标题：任务导向 */}
        <div
          style={{
            fontSize: 80,
            fontWeight: 800,
            letterSpacing: -3,
            lineHeight: 1.1,
            color: C.text,
            ...rise(local, 8, 14),
          }}
        >
          用 DeepSeek 桌面 Agent
        </div>
        <div
          style={{
            fontSize: 96,
            fontWeight: 800,
            letterSpacing: -4,
            lineHeight: 1.1,
            color: C.accent,
            marginTop: 4,
            ...rise(local, 14, 14),
          }}
        >
          自动化了一个真实任务
        </div>

        {/* 副标题：说明这是实测 */}
        <div
          style={{
            marginTop: 24,
            fontSize: 26,
            color: C.subtle,
            ...rise(local, 22, 12),
          }}
        >
          40秒看完 v1.5.1 所有修复 · 附完整复现步骤
        </div>

        {/* 数据标签 */}
        <div
          style={{
            marginTop: 28,
            display: "flex",
            gap: 16,
            ...rise(local, 28, 12),
          }}
        >
          {[
            { num: "7", label: "次提交" },
            { num: "9", label: "项修复" },
            { num: "v1.5.1", label: "发布" },
            { num: "0", label: "反代依赖" },
          ].map(({ num, label }) => (
            <span
              key={label}
              style={{
                padding: "8px 18px",
                borderRadius: 8,
                border: `1px solid ${C.border}`,
                background: "rgba(255,255,255,0.03)",
              }}
            >
              <span style={{ fontSize: 28, fontWeight: 700, color: C.text }}>{num}</span>
              <span style={{ marginLeft: 6, fontSize: 16, color: C.subtle }}>{label}</span>
            </span>
          ))}
        </div>
      </div>

      {/* 右侧：应用截图预览 */}
      <AppShot local={local} left={980} top={200} width={1020} delay={10} />
    </Scene>
  );
};

const Task1: React.FC = () => {
  const local = useCurrentFrame() - B[1];
  return (
    <Scene index={1}>
      <div style={{ position: "absolute", left: 110, top: 96 }}>
        <span style={{ fontFamily: MONO, fontSize: 22, color: C.subtle }}>Task 01 — 会话绑定</span>
        <div style={{ fontSize: 64, fontWeight: 800, letterSpacing: -2, color: C.text, ...rise(local, 8, 14) }}>
          多任务不再串台
        </div>
        <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 16, 12) }}>
          同时管理多个会话，输出归位
        </div>
      </div>

      <div style={{ position: "absolute", left: 110, top: 280, width: 820 }}>
        <TaskDemo
          local={local}
          task="同时回复两个会话的提问"
          input="同时发送两条消息到不同会话"
          result="各自独立回答，不串台"
          delay={20}
        />
        <TaskDemo
          local={local}
          task="队列消息按归属会话切回"
          input="排队 3 条消息到会话 A"
          result="自动切回 A，按顺序发送"
          delay={60}
        />
      </div>

      <AppShot
        local={local}
        left={980}
        top={200}
        width={1020}
        delay={30}
        focus={{ left: "0%", top: "0%", width: "22%", height: "100%" }}
        focusLabel="会话绑定"
      />
    </Scene>
  );
};

const Task2: React.FC = () => {
  const local = useCurrentFrame() - B[2];
  return (
    <Scene index={2}>
      <div style={{ position: "absolute", left: 110, top: 96 }}>
        <span style={{ fontFamily: MONO, fontSize: 22, color: C.subtle }}>Task 02 — 输入修复</span>
        <div style={{ fontSize: 64, fontWeight: 800, letterSpacing: -2, color: C.text, ...rise(local, 8, 14) }}>
          输入不再卡住
        </div>
        <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 16, 12) }}>
          v1.5.1 重点修复，三级兜底方案
        </div>
      </div>

      <div style={{ position: "absolute", left: 110, top: 280, width: 820 }}>
        <TaskCard
          local={local}
          step="FIX 1"
          title="三级兜底输入"
          subtitle="textarea → contenteditable → role=textbox"
          benefit="过滤不可见节点，确保粘贴内容干净"
        />
        <TaskCard
          local={local}
          step="FIX 2"
          title="发送不再静默失败"
          subtitle="按钮找不到立即报错并兜底回车"
          benefit="让用户知道发生了什么，而不是卡住"
        />
        <TaskCard
          local={local}
          step="FIX 3"
          title="Ctrl+Enter 按焦点分派"
          subtitle="对话页发送，会话页接续"
          benefit="符合直觉，不需要重新学习快捷键"
        />
      </div>

      <AppShot
        local={local}
        left={980}
        top={260}
        width={1020}
        delay={26}
        focus={{ left: "24%", top: "80%", width: "74%", height: "16%" }}
        focusLabel="输入卡片"
      />
    </Scene>
  );
};

const Task3: React.FC = () => {
  const local = useCurrentFrame() - B[3];
  return (
    <Scene index={3}>
      <div style={{ position: "absolute", left: 110, top: 96 }}>
        <span style={{ fontFamily: MONO, fontSize: 22, color: C.subtle }}>Task 03 — 附件顺序</span>
        <div style={{ fontSize: 64, fontWeight: 800, letterSpacing: -2, color: C.text, ...rise(local, 8, 14) }}>
          先确认输入可用，再上传
        </div>
        <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 16, 12) }}>
          修复附件在输入框不可用时无响应的边界问题
        </div>
      </div>

      <div style={{ position: "absolute", left: 110, top: 280, width: 760 }}>
        {[
          { action: "用户点击附件", step: "1", color: C.text },
          { action: "系统检查输入框状态", step: "2", color: C.subtle },
          { action: "输入框可用 → 显示附件预览", step: "3", color: C.ok },
          { action: "输入框不可用 → 提示用户激活", step: "3b", color: C.warn },
        ].map(({ action, step, color }, i) => (
          <div
            key={step}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 20,
              padding: "22px 0",
              borderBottom: i < 3 ? `1px solid ${C.border}` : "none",
              ...rise(local, 18 + i * 12, 12),
            }}
          >
            <span
              style={{
                fontFamily: MONO,
                fontSize: 18,
                color: C.accent,
                width: 40,
                textAlign: "center",
              }}
            >
              {step}
            </span>
            <span style={{ fontSize: 26, color }}>{action}</span>
          </div>
        ))}
      </div>

      <AppShot
        local={local}
        left={980}
        top={240}
        width={1020}
        delay={28}
        focus={{ left: "24%", top: "76%", width: "74%", height: "20%" }}
        focusLabel="附件区域"
      />
    </Scene>
  );
};

const Task4: React.FC = () => {
  const local = useCurrentFrame() - B[4];
  return (
    <Scene index={4}>
      <div style={{ position: "absolute", left: 110, top: 96 }}>
        <span style={{ fontFamily: MONO, fontSize: 22, color: C.subtle }}>Task 04 — 紧急停止</span>
        <div style={{ fontSize: 64, fontWeight: 800, letterSpacing: -2, color: C.text, ...rise(local, 8, 14) }}>
          红色急停，保留后续队列
        </div>
        <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 16, 12) }}>
          中断当前 PowerShell 任务，已排队消息不丢失
        </div>
      </div>

      <div style={{ position: "absolute", left: 110, top: 280, width: 800 }}>
        <TaskDemo
          local={local}
          task="Agent 执行卡死"
          input="点击红色 ■ 急停按钮"
          result="PowerShell 终止，队列保留"
          delay={22}
        />
        <div
          style={{
            marginTop: 24,
            padding: "20px 24px",
            background: "rgba(251,191,36,0.06)",
            border: `1px solid rgba(251,191,36,0.3)`,
            borderRadius: 10,
            fontSize: 24,
            color: C.warn,
            ...rise(local, 50),
          }}
        >
          💡 提示：急停后手动检查会话状态，再继续
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          right: 110,
          top: 280,
          padding: "28px 32px",
          background: "rgba(248,113,113,0.06)",
          border: `1px solid rgba(248,113,113,0.3)`,
          borderRadius: 12,
          maxWidth: 680,
          ...rise(local, 60),
        }}
      >
        <div style={{ fontSize: 18, color: C.warn, fontFamily: MONO, marginBottom: 10 }}>紧急停止逻辑</div>
        <div style={{ fontSize: 28, color: C.text, lineHeight: 1.5 }}>
          终止当前任务 → 清理 PowerShell 进程 → 保留队列状态 → 提示用户检查
        </div>
      </div>
    </Scene>
  );
};

const Task5: React.FC = () => {
  const local = useCurrentFrame() - B[5];
  return (
    <Scene index={5}>
      <div style={{ position: "absolute", left: 110, top: 96 }}>
        <span style={{ fontFamily: MONO, fontSize: 22, color: C.subtle }}>Task 05 — 待验证</span>
        <div style={{ fontSize: 64, fontWeight: 800, letterSpacing: -2, color: C.text, ...rise(local, 8, 14) }}>
          需要真实场景验证
        </div>
        <div style={{ marginTop: 10, fontSize: 24, color: C.subtle, ...rise(local, 16, 12) }}>
          输入链路与多会话的边界状态，下周连续跑测
        </div>
      </div>

      <div style={{ position: "absolute", left: 110, top: 280, width: 1700 }}>
        {[
          { status: "待验证", text: "输入框与 watchdog 仍有边界状态需连续跑测", color: C.warn },
          { status: "待验证", text: "多会话与队列刚修复，缺少长时间稳定性验证", color: C.warn },
          { status: "待验证", text: "网页关闭后的恢复路径还需更多真机场景", color: C.warn },
        ].map(({ status, text, color }, i) => (
          <div
            key={text}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 24,
              padding: "26px 0",
              borderBottom: i < 2 ? `1px solid ${C.border}` : "none",
              ...rise(local, 16 + i * 14, 14),
            }}
          >
            <span
              style={{
                padding: "6px 14px",
                borderRadius: 6,
                background: "rgba(251,191,36,0.08)",
                border: `1px solid rgba(251,191,36,0.4)`,
                fontSize: 16,
                color,
                fontFamily: MONO,
                minWidth: 100,
                textAlign: "center",
              }}
            >
              {status}
            </span>
            <span style={{ fontSize: 26, color: C.text }}>{text}</span>
          </div>
        ))}
      </div>
    </Scene>
  );
};

const Task6: React.FC = () => {
  const local = useCurrentFrame() - B[6];
  return (
    <Scene index={6}>
      <div style={{ position: "absolute", left: 110, top: 200, width: 820 }}>
        <div style={{ fontFamily: MONO, fontSize: 20, color: C.accent, letterSpacing: 4, ...rise(local, 6, 10) }}>
          NEXT · W45
        </div>
        <div style={{ fontSize: 100, fontWeight: 800, letterSpacing: -4, color: C.text, ...rise(local, 10, 14) }}>
          连续跑测
        </div>
        <div style={{ marginTop: 16, fontSize: 26, color: C.subtle, ...rise(local, 18, 12) }}>
          下一周重点：多会话、队列、重启恢复的稳定性验证
        </div>

        <div style={{ marginTop: 40, ...rise(local, 30, 14) }}>
          {[
            "连续跑测：多会话、队列、重启恢复",
            "详情页补充运行状态与诊断信息",
            "宣传片投放，回收首轮数据",
          ].map((t, i) => (
            <div
              key={t}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 18,
                padding: "18px 0",
                borderBottom: i < 2 ? `1px solid ${C.border}` : "none",
                fontSize: 26,
                color: C.text,
              }}
            >
              <span style={{ fontFamily: MONO, fontSize: 16, color: C.accent }}>
                {String(i + 1).padStart(2, "0")}
              </span>
              {t}
            </div>
          ))}
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          right: 110,
          top: 200,
          padding: "32px 36px",
          background: "rgba(77,141,255,0.06)",
          border: `1px solid rgba(77,141,255,0.3)`,
          borderRadius: 14,
          maxWidth: 640,
          ...rise(local, 40, 14),
        }}
      >
        <div style={{ fontSize: 18, color: C.accent, fontFamily: MONO, marginBottom: 12 }}>本周结论</div>
        <div style={{ fontSize: 30, color: C.text, lineHeight: 1.5 }}>
          输入链路与多任务绑定已收口，
          <br />
          <span style={{ color: C.ok }}>v1.5.1 可正常试用</span>
        </div>
      </div>
    </Scene>
  );
};

const Outro: React.FC = () => {
  const local = useCurrentFrame() - B[7];
  return (
    <Scene index={7}>
      <div style={{ position: "absolute", left: 110, top: 300 }}>
        <div style={{ fontFamily: MONO, fontSize: 20, color: C.accent, letterSpacing: 5, ...rise(local, 6, 10) }}>
          W44 · 2026 · 完整改版
        </div>
        <div style={{ marginTop: 16, fontSize: 110, fontWeight: 800, letterSpacing: -5, color: C.text, ...rise(local, 12, 14) }}>
          下周见
        </div>
        <div style={{ marginTop: 14, fontSize: 26, color: C.subtle, ...rise(local, 22) }}>
          全部改动都在真实界面里跑过，你能复现
        </div>
      </div>

      {/* CTA 区域 */}
      <div
        style={{
          position: "absolute",
          left: 110,
          top: 600,
          display: "flex",
          gap: 20,
          ...rise(local, 36),
        }}
      >
        <div
          style={{
            padding: "14px 28px",
            background: "rgba(77,141,255,0.1)",
            border: `1px solid ${C.accent2}`,
            borderRadius: 10,
            fontSize: 22,
            color: C.text,
          }}
        >
          💾 收藏视频 · 获取复现步骤
        </div>
        <div
          style={{
            padding: "14px 28px",
            background: "rgba(52,211,153,0.08)",
            border: `1px solid rgba(52,211,153,0.4)`,
            borderRadius: 10,
            fontSize: 22,
            color: C.ok,
          }}
        >
          💬 留言 · 反馈你的使用场景
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          right: 110,
          bottom: 110,
          fontFamily: MONO,
          fontSize: 18,
          color: C.subtle,
          ...rise(local, 48),
        }}
      >
        github.com/Meversation/Deepseek-Desktop-Agent
      </div>
    </Scene>
  );
};

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

export const WeeklyReport: React.FC = () => (
  <AbsoluteFill style={{ backgroundColor: "#06080D", fontFamily: FONT }}>
    <Backdrop />
    <Hero />
    <Task1 />
    <Task2 />
    <Task3 />
    <Task4 />
    <Task5 />
    <Task6 />
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
