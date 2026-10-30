import { Config } from "@remotion/cli/config";

Config.setVideoImageFormat("jpeg");
Config.setOverwriteOutput(true);
Config.setCodec("h264");
// Studio 固定 3001；渲染端口交给 Remotion 自动选择空闲端口，避免固定端口被占用
Config.setStudioPort(3001);