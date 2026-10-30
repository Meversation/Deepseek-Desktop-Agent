import { useCurrentFrame } from "remotion";

/**
 * 音轨已按 120 BPM 重制，30fps 下的整拍网格：
 * 1 拍 = 15 帧（0.5s），1 小节 = 60 帧（2s）。
 * 全片 28 小节 = 1680 帧 = 56s，所有场景切点都落在小节线上。
 */
export const BEAT = 15;
export const BAR = 60;
export const TOTAL_BARS = 28;

/** 每拍触发的脉冲：拍点处为 1，随后快速衰减 */
export const useBeatPulse = (decay = 4.2) => {
  const frame = useCurrentFrame();
  return Math.exp(-((frame % BEAT) / BEAT) * decay);
};

/** 每小节触发的重拍脉冲 */
export const useBarPulse = (decay = 2.6) => {
  const frame = useCurrentFrame();
  return Math.exp(-((frame % BAR) / BAR) * decay);
};
