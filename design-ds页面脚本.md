# DeepSeek 页面脚本（② 思考块 / ④ 深度思考）

所有脚本都在 CDP `Runtime.evaluate` 中执行，`returnByValue=true`，统一返回 JSON 字符串。
选择器集中在 `SELECTORS` 常量区，页面改版时只改这一处。结构变化时返回 `{ok:false, reason:"..."}`，不抛异常。

```js
const SELECTORS = {
  reply: '.ds-assistant-message-main-content, [class*="ds-assistant-message-main-content"]',
  // 思考块容器：优先语义类名，其次按文案兜底（下方 findThinking 内还会做文本扫描）
  thinking: '[class*="thinking"],[class*="reason"],[class*="think-block"],[class*="thought"]',
  thinkingSummary: '[class*="thinking"] [class*="summary"],[class*="thought"] [class*="title"]',
  deepThink: '[role="button"],[role="switch"],button,div'
};
```

## 1. readReply()

```js
(() => {
  const nodes = document.querySelectorAll(SELECTORS.reply);
  if (!nodes.length) return JSON.stringify({ ok: false, reason: 'reply-not-found' });
  const text = nodes[nodes.length - 1].innerText || '';
  return JSON.stringify({ ok: true, text });
})()
```

## 2. readThinking()

按“语义类名 → 文案定位”两级兜底。文案定位会向上找 3 层，取包含“思考”标题与正文的最近容器。

```js
(() => {
  const isSummary = t => /(已深度思考|思考中|已思考|深度思考)/.test(t) && t.length < 60;
  let host = null;
  for (const el of document.querySelectorAll(SELECTORS.thinking)) {
    if ((el.innerText || '').trim().length > 0) { host = el; }
  }
  if (!host) {
    // 文案兜底：找到最后一个“已深度思考（用时 … 秒）”元素，再上溯到有正文的容器
    const all = [...document.querySelectorAll('div,span,p')].filter(el => isSummary((el.innerText || '').trim()));
    const last = all[all.length - 1];
    if (last) {
      let up = last;
      for (let i = 0; i < 3 && up.parentElement; i++) up = up.parentElement;
      host = up;
    }
  }
  if (!host) return JSON.stringify({ ok: false, reason: 'thinking-not-found' });
  const raw = host.innerText || '';
  const lines = raw.split('\n').map(s => s.trim()).filter(Boolean);
  const summary = lines.find(isSummary) || '';
  const duration = (summary.match(/用时\s*(\d+)\s*(秒|分|分钟)/) || []);
  let seconds = null;
  if (duration.length) seconds = duration[2] === '秒' ? +duration[1] : +duration[1] * 60;
  const body = lines.filter(l => l !== summary).join('\n');
  return JSON.stringify({ ok: true, summary, body, durationSeconds: seconds, complete: !/思考中/.test(summary) });
})()
```

## 3. readDeepThinkState()

```js
(() => {
  const keywords = ['深度思考', 'DeepThink', 'deep think'];
  const hit = [...document.querySelectorAll(SELECTORS.deepThink)].find(el => {
    const t = (el.innerText || '').trim();
    return t.length <= 12 && keywords.some(k => t.includes(k));
  });
  if (!hit) return JSON.stringify({ ok: false, reason: 'deep-think-not-found' });
  const on = hit.getAttribute('aria-pressed') === 'true' ||
             hit.getAttribute('aria-checked') === 'true' ||
             /active|checked|selected|open/i.test(hit.className || '');
  return JSON.stringify({ ok: true, on });
})()
```

## 4. setDeepThink(on)

只在状态不一致时点击，点击前后各读一次状态，超时返回失败。

```js
(() => {
  const keywords = ['深度思考', 'DeepThink', 'deep think'];
  const hit = [...document.querySelectorAll(SELECTORS.deepThink)].find(el => {
    const t = (el.innerText || '').trim();
    return t.length <= 12 && keywords.some(k => t.includes(k));
  });
  if (!hit) return JSON.stringify({ ok: false, reason: 'deep-think-not-found' });
  const isOn = () => hit.getAttribute('aria-pressed') === 'true' ||
                     hit.getAttribute('aria-checked') === 'true' ||
                     /active|checked|selected|open/i.test(hit.className || '');
  if (isOn() === !!on) return JSON.stringify({ ok: true, changed: false, on: isOn() });
  hit.click();
  return JSON.stringify({ ok: true, changed: true, on: isOn() });
})()
```

若点击后状态未变，说明按钮内部是 `mousedown`/`mouseup` 逻辑，需要在本地改用 `Input.dispatchMouseEvent` 走真实指针事件。

## 5. clickSend()

优先复用 `CdpClient` 已有的发送逻辑（它已处理输入框同步与按钮选择），本脚本只作为兜底：

```js
(() => {
  const btn = [...document.querySelectorAll('[role="button"],button,div')].find(el => {
    const t = (el.innerText || '').trim();
    return t === '发送' || el.getAttribute('aria-label') === '发送' || el.getAttribute('aria-label') === 'Send';
  });
  if (!btn) return JSON.stringify({ ok: false, reason: 'send-not-found' });
  btn.click();
  return JSON.stringify({ ok: true });
})()
```

## 6. 接入约定

- `readThinking()` 的输出交给 `ThinkingBlockParser.ParseText(body, summary, durationSeconds, complete)`，不要抛异常。
- `setDeepThink` 只在用户点击“深度思考”开关时调用，不在每轮自动切换。
- 页面改版后先跑 `readThinking()`：若返回 `thinking-not-found`，抓一次 DOM 快照再更新 `SELECTORS`。
