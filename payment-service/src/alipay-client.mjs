import { AlipaySdk } from "alipay-sdk";
import { assertAlipayConfig } from "./config.mjs";
import { createOutTradeNo } from "./alipay-orders.mjs";

const ALIPAY_SUCCESS_STATUSES = new Set([
  "TRADE_SUCCESS",
  "TRADE_FINISHED",
]);

export function createAlipayClient(config) {
  assertAlipayConfig(config);

  return new AlipaySdk({
    appId: config.alipayAppId,
    privateKey: config.alipayPrivateKey,
    alipayPublicKey: config.alipayPublicKey,
    keyType: config.alipayKeyType,
    endpoint: config.alipayGateway,
  });
}

export function buildAlipayCheckoutParams(config, order, overrides = {}) {
  const amount = Number(overrides.amount || config.alipayAmount);
  if (!Number.isFinite(amount) || amount <= 0) {
    throw new Error("Alipay amount must be a positive number.");
  }

  const outTradeNo = String(order?.outTradeNo || createOutTradeNo());
  const subject = String(
    overrides.subject || order?.subject || config.alipaySubject,
  ).slice(0, 256);

  const returnUrl = (
    overrides.returnUrl
    || `${config.publicBaseUrl}/payment/alipay/return?out_trade_no=${encodeURIComponent(outTradeNo)}`
  ).replace(/\/+$/, "");

  return {
    outTradeNo,
    bizContent: {
      out_trade_no: outTradeNo,
      product_code: "FAST_INSTANT_TRADE_PAY",
      subject,
      total_amount: amount.toFixed(2),
    },
    returnUrl,
  };
}

/**
 * 生成电脑网站支付（alipay.trade.page.pay）的自动提交表单 HTML。
 * 浏览器打开该页面后会直接跳转到支付宝收银台。
 */
export function buildAlipayCheckoutHtml(client, config, order, overrides = {}) {
  const { outTradeNo, bizContent, returnUrl } = buildAlipayCheckoutParams(
    config,
    order,
    overrides,
  );

  const html = client.pageExecute("alipay.trade.page.pay", "POST", {
    bizContent,
    returnUrl,
  });

  return { html, outTradeNo };
}

/**
 * 主动查询支付宝订单状态（alipay.trade.query）。
 * 桌面应用收不到支付宝的异步通知，因此用本地主动查单确认到账。
 */
export async function queryAlipayOrder(client, outTradeNo) {
  const result = await client.exec("alipay.trade.query", {
    bizContent: { out_trade_no: outTradeNo },
  });

  const response =
    result?.alipayTradeQueryResponse ||
    result?.alipay_trade_query_response ||
    result || {};

  return {
    code: String(response.code || ""),
    message: String(response.msg || response.subMsg || ""),
    tradeStatus: String(response.tradeStatus || response.trade_status || ""),
    outTradeNo: String(response.outTradeNo || response.out_trade_no || ""),
    tradeNo: String(response.tradeNo || response.trade_no || ""),
    totalAmount: String(response.totalAmount || response.total_amount || ""),
    paid: ALIPAY_SUCCESS_STATUSES.has(
      String(response.tradeStatus || response.trade_status || ""),
    ),
  };
}

export function isAlipaySuccessStatus(tradeStatus) {
  return ALIPAY_SUCCESS_STATUSES.has(String(tradeStatus));
}
