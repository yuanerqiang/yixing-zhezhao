import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadConfig, hasAlipayCredentials } from "../src/config.mjs";
import {
  createAlipayClient,
  buildAlipayCheckoutHtml,
  queryAlipayOrder,
} from "../src/alipay-client.mjs";
import { AlipayOrderStore } from "../src/alipay-orders.mjs";

const serviceRoot = path.resolve(
  fileURLToPath(new URL("..", import.meta.url)),
);

async function main() {
  const config = loadConfig();

  if (!hasAlipayCredentials(config)) {
    console.error(
      "[支付宝自检] 失败：ALIPAY_APP_ID / ALIPAY_PRIVATE_KEY / ALIPAY_PUBLIC_KEY 未填写或仍是占位值。",
    );
    console.error("请先到支付宝开放平台创建应用、签约「电脑网站支付」，再把密钥填入 .env。");
    process.exitCode = 1;
    return;
  }

  console.log(
    `[支付宝自检] 配置已读取：环境=${config.alipayEnvironment} 金额=${config.alipayAmount.toFixed(2)}元 网关=${config.alipayGateway}`,
  );

  try {
    const client = createAlipayClient(config);
    const orderStore = new AlipayOrderStore(
      path.join(config.dataDir, "alipay-orders.jsonl"),
    ).init();

    // 1) 本地签名 + 生成支付表单（不联网，验证密钥格式）
    const order = await orderStore.create({
      installationId: "verify-only",
      amount: config.alipayAmount,
      subject: config.alipaySubject,
    });
    const { html } = buildAlipayCheckoutHtml(client, config, order);
    if (!html.includes("alipay") || !html.includes("biz_content")) {
      throw new Error("生成的支付表单结构异常。");
    }
    console.log("[支付宝自检] 通过：支付表单签名生成成功（未实际扣款）。");

    // 2) 联网查单（查询一个不存在的订单；若签名/网关正确会返回“交易不存在”）
    const result = await queryAlipayOrder(client, order.outTradeNo);
    console.log(`[支付宝自检] 查单接口返回：code=${result.code} status=${result.tradeStatus || "-"}`);
    if (result.code === "10000" || result.code === "40004") {
      console.log("[支付宝自检] 通过：网关连通，签名校验被支付宝接受。");
    } else {
      console.warn(
        "[支付宝自检] 提示：网关返回了非预期结果，请核对密钥与网关配置。",
      );
      console.warn(`  响应详情：${result.message || "无"}`);
    }

    console.log(
      "\n完成。可以运行 npm start 启动服务，然后在浏览器打开 http://127.0.0.1:8787/alipay/buy 测试支付页。",
    );
  } catch (error) {
    console.error("[支付宝自检] 失败：", error instanceof Error ? error.message : error);
    console.error("常见原因：密钥格式不对（试试 ALIPAY_KEY_TYPE=PKCS1）、网关地址不对、私钥复制不完整。");
    process.exitCode = 1;
  }
}

main();
