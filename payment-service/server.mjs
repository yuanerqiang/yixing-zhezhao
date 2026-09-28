import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  assertCheckoutConfig,
  assertAlipayConfig,
  hasApiCredentials,
  hasAlipayCredentials,
  loadConfig,
} from "./src/config.mjs";
import { EventStore } from "./src/event-store.mjs";
import { getInstallationId } from "./src/installation.mjs";
import { createWaffoClient } from "./src/waffo-client.mjs";
import {
  createAlipayClient,
  buildAlipayCheckoutHtml,
  queryAlipayOrder,
} from "./src/alipay-client.mjs";
import { AlipayOrderStore } from "./src/alipay-orders.mjs";
import {
  AlipayDeviceStore,
  computeTrialState,
} from "./src/alipay-devices.mjs";
import { verifyWebhook } from "@waffo/pancake-ts";

const MAX_JSON_BODY_BYTES = 64 * 1024;
const MAX_WEBHOOK_BODY_BYTES = 1024 * 1024;

function sendJson(response, statusCode, payload) {
  const body = JSON.stringify(payload, null, 2);
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(body),
    "Cache-Control": "no-store",
  });
  response.end(body);
}

function sendTextState(response, state) {
  const lines = [
    `trialActive=${state.trialActive ? "true" : "false"}`,
    `paid=${state.paid ? "true" : "false"}`,
    `price=${Number(state.price).toFixed(2)}`,
    `daysUsed=${Number(state.daysUsed) || 0}`,
    `trialDays=${Number(state.trialDays) || 0}`,
    `firstUseAt=${state.firstUseAt || ""}`,
  ];
  const body = lines.join("\n");
  response.writeHead(200, {
    "Content-Type": "text/plain; charset=utf-8",
    "Content-Length": Buffer.byteLength(body),
    "Cache-Control": "no-store",
  });
  response.end(body);
}

function sendHtml(response, statusCode, html) {
  response.writeHead(statusCode, {
    "Content-Type": "text/html; charset=utf-8",
    "Content-Length": Buffer.byteLength(html),
    "Cache-Control": "no-store",
  });
  response.end(html);
}

function htmlEscape(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function readBody(request, limitBytes) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;

    request.on("data", (chunk) => {
      size += chunk.length;
      if (size > limitBytes) {
        reject(new Error("Request body is too large."));
        request.destroy();
        return;
      }
      chunks.push(chunk);
    });
    request.on("end", () => resolve(Buffer.concat(chunks)));
    request.on("error", reject);
  });
}

function sanitizeMetadata(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return {};
  }

  const result = {};
  for (const [key, rawValue] of Object.entries(value)) {
    if (Object.keys(result).length >= 20) {
      break;
    }
    if (!/^[A-Za-z0-9_.-]{1,40}$/.test(key)) {
      continue;
    }

    const stringValue = String(rawValue);
    if (stringValue.length <= 500) {
      result[key] = stringValue;
    }
  }
  return result;
}

export function buildCheckoutParams(config, installationId, overrides = {}) {
  const productId = String(overrides.productId || config.productId || "").trim();
  const currency = String(overrides.currency || config.currency || "USD")
    .trim()
    .toUpperCase();
  const successUrl = String(
    overrides.successUrl || `${config.publicBaseUrl}/payment/success`,
  );

  if (!config.storeId) {
    throw new Error("WAFFO_STORE_ID is missing.");
  }
  if (!productId) {
    throw new Error("WAFFO_ONETIME_PRODUCT_ID is missing.");
  }
  if (!/^https?:\/\//i.test(successUrl)) {
    throw new Error("successUrl must be an http or https URL.");
  }

  const params = {
    storeId: config.storeId,
    productId,
    productType: "onetime",
    currency,
    buyerIdentity: installationId,
    successUrl,
    darkMode: true,
    metadata: {
      ...sanitizeMetadata(overrides.metadata),
      installationId,
      source: "study-mask",
    },
  };

  const buyerEmail = String(overrides.buyerEmail || "").trim();
  if (buyerEmail) {
    params.buyerEmail = buyerEmail;
  }

  return params;
}

function renderSetupPage(config, errorMessage) {
  const missing = [
    !config.merchantId && "WAFFO_MERCHANT_ID",
    (!config.privateKey || config.privateKey.includes("<paste-your-private-key>"))
      && "WAFFO_PRIVATE_KEY",
    !config.storeId && "WAFFO_STORE_ID",
    !config.productId && "WAFFO_ONETIME_PRODUCT_ID",
  ].filter(Boolean);

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Waffo Pancake setup required</title>
  <style>
    body { margin: 0; background: #111412; color: #f4f6f3; font: 16px/1.55 system-ui, sans-serif; }
    main { max-width: 720px; margin: 10vh auto; padding: 28px; }
    h1 { font-size: 28px; margin: 0 0 16px; }
    code, pre { background: #202521; color: #d9f5c6; border-radius: 6px; }
    code { padding: 2px 6px; }
    pre { padding: 16px; overflow: auto; }
    a { color: #9ee225; }
  </style>
</head>
<body>
  <main>
    <h1>Payment setup required</h1>
    <p>${htmlEscape(errorMessage || "The local payment service is not fully configured.")}</p>
    ${missing.length
      ? `<p>Missing or placeholder values: <code>${missing.map(htmlEscape).join("</code>, <code>")}</code></p>`
      : ""}
    <p>In <code>payment-service</code>, copy <code>.env.example</code> to <code>.env</code>, paste the RSA private key, then create or select a one-time product.</p>
    <pre>npm install
npm run setup:product -- --amount 9.90 --currency USD --write-env
npm start</pre>
    <p>For webhooks, expose the service with an HTTPS tunnel and run:</p>
    <pre>npm run setup:webhook -- --url https://your-public-domain.example/api/webhooks/waffo</pre>
  </main>
</body>
</html>`;
}

function renderAlipaySetupPage(config, errorMessage) {
  const missing = [
    !config.alipayAppId && "ALIPAY_APP_ID",
    (!config.alipayPrivateKey
      || config.alipayPrivateKey.includes("<paste-your-alipay-private-key>"))
      && "ALIPAY_PRIVATE_KEY",
    (!config.alipayPublicKey
      || config.alipayPublicKey.includes("<paste-your-alipay-public-key>"))
      && "ALIPAY_PUBLIC_KEY",
  ].filter(Boolean);

  return `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>支付宝配置尚未完成</title>
  <style>
    body { margin: 0; background: #111412; color: #f4f6f3; font: 16px/1.55 system-ui, sans-serif; }
    main { max-width: 720px; margin: 10vh auto; padding: 28px; }
    h1 { font-size: 26px; margin: 0 0 16px; }
    code, pre { background: #202521; color: #d9f5c6; border-radius: 6px; }
    code { padding: 2px 6px; }
    pre { padding: 16px; overflow: auto; }
    a { color: #9ee225; }
  </style>
</head>
<body>
  <main>
    <h1>支付宝配置尚未完成</h1>
    <p>${htmlEscape(errorMessage || "本地支付服务还没有配置支付宝。" )}</p>
    ${missing.length
      ? `<p>缺少或仍是占位值：<code>${missing.map(htmlEscape).join("</code>、<code>")}</code></p>`
      : ""}
    <p>请在 <code>payment-service</code> 目录下，把 <code>.env</code> 中的支付宝配置项替换为你在支付宝开放平台申请的正式值（应用 ID、应用私钥、支付宝公钥），然后重启服务。</p>
    <pre>npm run verify:alipay
npm start</pre>
  </main>
</body>
</html>`;
}

function renderAlipayPaymentResult(config, outTradeNo) {
  return `<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>支付结果</title>
  <style>
    body { margin: 0; background: #f4f6f2; color: #182019; font: 16px/1.55 system-ui, sans-serif; }
    main { max-width: 620px; margin: 12vh auto; padding: 32px; background: white; border: 1px solid #dde3da; border-radius: 8px; text-align: center; }
    h1 { margin: 0 0 12px; font-size: 26px; }
    p { margin: 6px 0; color: #4d584f; }
    .spinner { display: inline-block; width: 22px; height: 22px; border: 3px solid #dde3da; border-top-color: #1677ff; border-radius: 50%; animation: spin 0.9s linear infinite; margin-bottom: 8px; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .ok { color: #1a7f37; font-weight: 700; }
    .pending { color: #b45309; font-weight: 700; }
  </style>
</head>
<body>
  <main>
    <h1>学习遮罩 · 支付宝支付</h1>
    <div id="spinner" class="spinner"></div>
    <p id="status" class="pending">正在确认支付结果…</p>
    <p id="detail">请稍候，正在向支付宝核实这笔订单。</p>
  </main>
  <script>
    (function () {
      var outTradeNo = new URLSearchParams(location.search).get("out_trade_no");
      var status = document.getElementById("status");
      var detail = document.getElementById("detail");
      var spinner = document.getElementById("spinner");
      var attempts = 0;

      async function poll() {
        if (!outTradeNo) {
          status.textContent = "缺少订单号";
          status.className = "pending";
          detail.textContent = "无法确认这笔订单，请返回应用重试。";
          spinner.style.display = "none";
          return;
        }

        try {
          var response = await fetch(
            "/api/alipay/verify?out_trade_no=" + encodeURIComponent(outTradeNo),
            { cache: "no-store" }
          );
          var data = await response.json();

          if (data && data.paid) {
            status.textContent = "支付成功，感谢购买！";
            status.className = "ok";
            detail.textContent = "你现在可以关闭这个页面，回到学习遮罩使用全部功能。";
            spinner.style.display = "none";
            return;
          }

          attempts += 1;
          if (attempts >= 15) {
            status.textContent = "尚未检测到支付成功";
            status.className = "pending";
            detail.textContent = "如果你已经完成付款，请稍后重启学习遮罩或重新点击购买确认。";
            spinner.style.display = "none";
            return;
          }

          detail.textContent = "订单还未支付完成，正在继续确认…（第 " + attempts + " 次）";
          setTimeout(poll, 2000);
        } catch (error) {
          attempts += 1;
          if (attempts >= 6) {
            status.textContent = "无法连接支付服务";
            status.className = "pending";
            detail.textContent = "请确认本地支付服务正在运行，然后重新打开这个页面。";
            spinner.style.display = "none";
            return;
          }
          setTimeout(poll, 1500);
        }
      }

      poll();
    })();
  </script>
</body>
</html>`;
}

function renderPaymentResult(title, message) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${htmlEscape(title)}</title>
  <style>
    body { margin: 0; background: #f4f6f2; color: #182019; font: 16px/1.55 system-ui, sans-serif; }
    main { max-width: 620px; margin: 12vh auto; padding: 32px; background: white; border: 1px solid #dde3da; border-radius: 8px; }
    h1 { margin: 0 0 12px; font-size: 28px; }
    p { margin: 0; color: #4d584f; }
  </style>
</head>
<body>
  <main>
    <h1>${htmlEscape(title)}</h1>
    <p>${htmlEscape(message)}</p>
  </main>
</body>
</html>`;
}

function getEntitlement(events, installationId, mode, alipayMode) {
  let granted = false;
  let refunded = false;

  for (const event of events) {
    const eventInstallationId = event?.data?.metadata?.installationId;
    const eventMode = event?.mode;
    if (
      eventInstallationId !== installationId
      || (eventMode !== mode && eventMode !== alipayMode)
    ) {
      continue;
    }

    if (
      event.eventType === "order.completed"
      || event.eventType === "subscription.activated"
      || event.eventType === "subscription.payment_succeeded"
      || event.eventType === "alipay.order.completed"
    ) {
      granted = true;
    } else if (event.eventType === "refund.succeeded") {
      refunded = true;
    }
  }

  return {
    installationId,
    active: granted && !refunded,
    granted,
    refunded,
  };
}

async function handleRequest(request, response, dependencies) {
  const {
    config,
    client,
    alipayClient,
    eventStore,
    alipayOrderStore,
    alipayDeviceStore,
  } = dependencies;
  const url = new URL(request.url || "/", config.publicBaseUrl);

  if (request.method === "GET" && url.pathname === "/health") {
    return sendJson(response, 200, {
      ok: true,
      service: "study-mask-payment-service",
      mode: config.mode,
      apiConfigured: hasApiCredentials(config),
      checkoutConfigured: hasApiCredentials(config) && Boolean(config.productId),
      storeId: config.storeId || null,
      productId: config.productId || null,
      alipayConfigured: hasAlipayCredentials(config),
      alipayEnvironment: config.alipayEnvironment,
      alipayAmount: config.alipayAmount.toFixed(2),
      alipayTrialAmount: config.alipayTrialAmount.toFixed(2),
      alipayTrialDays: config.alipayTrialDays,
    });
  }

  if (request.method === "GET" && url.pathname === "/buy") {
    try {
      assertCheckoutConfig(config);
      const machineId = (url.searchParams.get("machineId") || "").trim()
        || getInstallationId(config);
      const device = await alipayDeviceStore.register(machineId, null);
      const state = computeTrialState(config, device);

      let productId = config.productId;
      if (config.trialProductId && state.trialActive && !state.paid) {
        productId = config.trialProductId;
      }

      const result = await client.checkout.authenticated.create(
        buildCheckoutParams(config, machineId, { productId }),
      );
      response.writeHead(302, {
        Location: result.checkoutUrl,
        "Cache-Control": "no-store",
      });
      return response.end();
    } catch (error) {
      return sendHtml(
        response,
        503,
        renderSetupPage(config, error instanceof Error ? error.message : String(error)),
      );
    }
  }

  if (request.method === "GET" && url.pathname === "/alipay/buy") {
    try {
      assertAlipayConfig(config);
      const machineId = (url.searchParams.get("machineId") || "").trim()
        || getInstallationId(config);
      const firstUseHint = url.searchParams.get("firstUse") || "";
      const device = await alipayDeviceStore.register(
        machineId,
        firstUseHint || null,
      );
      const trial = computeTrialState(config, device);
      const order = await alipayOrderStore.create({
        installationId: machineId,
        amount: trial.price,
        subject: config.alipaySubject,
      });
      const { html } = buildAlipayCheckoutHtml(alipayClient, config, order, {
        amount: trial.price,
      });
      return sendHtml(response, 200, html);
    } catch (error) {
      return sendHtml(
        response,
        503,
        renderAlipaySetupPage(config, error instanceof Error ? error.message : String(error)),
      );
    }
  }

  if (
    (request.method === "GET" || request.method === "POST")
    && url.pathname === "/api/alipay/register"
  ) {
    try {
      let machineId = "";
      let firstUseHint = "";
      if (request.method === "POST") {
        const rawBody = await readBody(request, MAX_JSON_BODY_BYTES);
        const body = rawBody.length ? JSON.parse(rawBody.toString("utf8")) : {};
        machineId = String(body.machineId || "").trim();
        firstUseHint = body.firstUse ? String(body.firstUse) : "";
      } else {
        machineId = (url.searchParams.get("machineId") || "").trim();
        firstUseHint = url.searchParams.get("firstUse") || "";
      }

      if (!machineId) {
        return sendJson(response, 400, { error: "machineId is required." });
      }
      const device = await alipayDeviceStore.register(
        machineId,
        firstUseHint || null,
      );
      const state = computeTrialState(config, device);
      if (url.searchParams.get("format") === "text") {
        return sendTextState(response, state);
      }
      return sendJson(response, 200, state);
    } catch (error) {
      return sendJson(response, 400, {
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  if (request.method === "GET" && url.pathname === "/api/alipay/trial") {
    const machineId = (url.searchParams.get("machineId") || "").trim();
    if (!machineId) {
      return sendJson(response, 400, { error: "machineId is required." });
    }
    const device = alipayDeviceStore.get(machineId);
    const state = computeTrialState(config, device);
    if (url.searchParams.get("format") === "text") {
      return sendTextState(response, state);
    }
    return sendJson(response, 200, state);
  }

  if (request.method === "GET" && url.pathname === "/payment/alipay/return") {
    const outTradeNo = url.searchParams.get("out_trade_no") || "";
    return sendHtml(
      response,
      200,
      renderAlipayPaymentResult(config, outTradeNo),
    );
  }

  if (request.method === "GET" && url.pathname === "/api/alipay/verify") {
    const outTradeNo = url.searchParams.get("out_trade_no");
    if (!outTradeNo) {
      return sendJson(response, 400, { error: "out_trade_no is required." });
    }

    try {
      assertAlipayConfig(config);
      const order = alipayOrderStore.get(outTradeNo);
      if (!order) {
        return sendJson(response, 404, { error: "Order not found." });
      }

      const query = await queryAlipayOrder(alipayClient, outTradeNo);
      if (query.paid) {
        alipayOrderStore.markPaid(outTradeNo, query.tradeNo, query.tradeStatus);
        alipayDeviceStore.markPaid(order.installationId, outTradeNo);
        await eventStore.record({
          id: `alipay-${outTradeNo}`,
          mode: config.alipayEnvironment,
          eventType: "alipay.order.completed",
          data: {
            metadata: {
              installationId: order.installationId,
              outTradeNo,
              tradeNo: query.tradeNo,
              amount: query.totalAmount || String(order.amount),
              source: "study-mask-alipay",
            },
          },
        });
      }

      return sendJson(response, 200, {
        paid: query.paid,
        tradeStatus: query.tradeStatus,
        order: {
          outTradeNo: order.outTradeNo,
          amount: String(order.amount),
          status: order.status,
        },
      });
    } catch (error) {
      return sendJson(response, 400, {
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  if (request.method === "GET" && url.pathname === "/api/alipay/orders") {
    const installationId = url.searchParams.get("installationId")
      || getInstallationId(config);
    return sendJson(response, 200, {
      orders: alipayOrderStore.listByInstallationId(installationId),
    });
  }

  if (request.method === "POST" && url.pathname === "/api/checkout") {
    try {
      assertCheckoutConfig(config);
      const rawBody = await readBody(request, MAX_JSON_BODY_BYTES);
      const overrides = rawBody.length
        ? JSON.parse(rawBody.toString("utf8"))
        : {};
      const installationId = getInstallationId(config);
      const result = await client.checkout.authenticated.create(
        buildCheckoutParams(config, installationId, overrides),
      );
      return sendJson(response, 201, {
        checkoutUrl: result.checkoutUrl,
        sessionId: result.sessionId,
        expiresAt: result.expiresAt,
      });
    } catch (error) {
      return sendJson(response, 400, {
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  if (request.method === "POST" && url.pathname === "/api/webhooks/waffo") {
    try {
      const rawBody = await readBody(request, MAX_WEBHOOK_BODY_BYTES);
      const signature = request.headers["x-waffo-signature"];
      const event = verifyWebhook(rawBody.toString("utf8"), signature, {
        environment: config.mode,
        toleranceMs: config.webhookToleranceMs,
      });
      const stored = await eventStore.record(event);

      if (event?.eventType === "order.completed") {
        const installationId = event?.data?.metadata?.installationId;
        if (installationId) {
          alipayDeviceStore.markPaid(String(installationId), event.id || "waffo");
        }
      }

      return sendJson(response, 200, { ok: true, duplicate: !stored });
    } catch (error) {
      return sendJson(response, 401, {
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  if (request.method === "GET" && url.pathname === "/api/events") {
    const requestedInstallationId = url.searchParams.get("installationId");
    const events = eventStore.readAll().filter((event) => {
      if (!requestedInstallationId) {
        return true;
      }
      return event?.data?.metadata?.installationId === requestedInstallationId;
    });
    return sendJson(response, 200, { events: events.slice(-100) });
  }

  if (request.method === "GET" && url.pathname === "/api/entitlement") {
    const installationId = url.searchParams.get("installationId")
      || getInstallationId(config);
    return sendJson(
      response,
      200,
      getEntitlement(
        eventStore.readAll(),
        installationId,
        config.mode,
        config.alipayEnvironment,
      ),
    );
  }

  if (request.method === "GET" && url.pathname === "/payment/success") {
    return sendHtml(
      response,
      200,
      renderPaymentResult(
        "Payment completed",
        "You can close this page and return to Study Mask.",
      ),
    );
  }

  if (request.method === "GET" && url.pathname === "/payment/cancel") {
    return sendHtml(
      response,
      200,
      renderPaymentResult(
        "Payment canceled",
        "No payment was completed. You can return to Study Mask.",
      ),
    );
  }

  return sendJson(response, 404, { error: "Not found" });
}

export function createPaymentServer(dependencies) {
  return http.createServer((request, response) => {
    handleRequest(request, response, dependencies).catch((error) => {
      if (!response.headersSent) {
        sendJson(response, 500, {
          error: error instanceof Error ? error.message : String(error),
        });
      } else {
        response.destroy(error);
      }
    });
  });
}

async function start() {
  const config = loadConfig();
  const client = hasApiCredentials(config) ? createWaffoClient(config) : null;
  const alipayClient = hasAlipayCredentials(config)
    ? createAlipayClient(config)
    : null;
  const eventStore = new EventStore(
    path.join(config.dataDir, "webhook-events.jsonl"),
  ).init();
  const alipayOrderStore = new AlipayOrderStore(
    path.join(config.dataDir, "alipay-orders.jsonl"),
  ).init();
  const alipayDeviceStore = new AlipayDeviceStore(
    path.join(config.dataDir, "alipay-devices.jsonl"),
  ).init();
  const server = createPaymentServer({
    config,
    client,
    alipayClient,
    eventStore,
    alipayOrderStore,
    alipayDeviceStore,
  });

  server.listen(config.port, config.host, () => {
    console.log(
      `Study Mask payment service listening on http://${config.host}:${config.port} (${config.mode})`,
    );
    console.log(
      `Alipay: ${hasAlipayCredentials(config)
        ? `configured (${config.alipayEnvironment}, ${config.alipayAmount.toFixed(2)} CNY)`
        : "not configured — set ALIPAY_* in .env to enable"}`,
    );
  });

  const shutdown = () => server.close(() => process.exit(0));
  process.on("SIGINT", shutdown);
  process.on("SIGTERM", shutdown);
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isMain) {
  start().catch((error) => {
    console.error(error instanceof Error ? error.stack : error);
    process.exit(1);
  });
}
