import { parseArgs } from "node:util";
import {
  assertApiConfig,
  loadConfig,
  updateEnvironmentValue,
} from "../src/config.mjs";
import { createWaffoClient } from "../src/waffo-client.mjs";

const { values } = parseArgs({
  options: {
    name: { type: "string", default: "Study Mask" },
    description: {
      type: "string",
      default: "One-time license for Study Mask desktop application.",
    },
    amount: { type: "string" },
    currency: { type: "string" },
    "tax-category": { type: "string" },
    "success-url": { type: "string" },
    "write-env": { type: "boolean", default: false },
  },
  allowPositionals: false,
});

const config = loadConfig();
assertApiConfig(config);

const amount = String(values.amount || process.env.WAFFO_PRODUCT_AMOUNT || "").trim();
const currency = String(values.currency || config.currency).trim().toUpperCase();
const taxCategory = String(values["tax-category"] || config.taxCategory).trim();
const successUrl = String(
  values["success-url"] || `${config.publicBaseUrl}/payment/success`,
).trim();

if (!amount) {
  throw new Error(
    "Product amount is required. Example: npm run setup:product -- --amount 9.90 --currency USD",
  );
}
if (!/^\d+(?:\.\d+)?$/.test(amount)) {
  throw new Error("Product amount must be a display amount such as 9.90.");
}
if (!/^[A-Z]{3}$/.test(currency)) {
  throw new Error("Currency must be an ISO 4217 code such as USD.");
}
if (!/^https?:\/\//i.test(successUrl)) {
  throw new Error("Success URL must be an http or https URL.");
}

const client = createWaffoClient(config);
const { product } = await client.onetimeProducts.create({
  storeId: config.storeId,
  name: values.name,
  description: values.description,
  prices: {
    [currency]: {
      amount,
      taxCategory,
    },
  },
  successUrl,
  metadata: {
    source: "study-mask",
  },
});

if (values["write-env"]) {
  updateEnvironmentValue("WAFFO_ONETIME_PRODUCT_ID", product.id);
}

console.log(JSON.stringify(product, null, 2));
console.log("");
console.log(`WAFFO_ONETIME_PRODUCT_ID=${product.id}`);
if (!values["write-env"]) {
  console.log("Add that value to payment-service/.env, or rerun with --write-env.");
}
