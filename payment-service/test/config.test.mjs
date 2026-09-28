import assert from "node:assert/strict";
import test from "node:test";
import { parseEnvFile } from "../src/config.mjs";
import { buildCheckoutParams } from "../server.mjs";

test("parseEnvFile handles comments, quotes, and escaped newlines", () => {
  const values = parseEnvFile(`
# comment
WAFFO_MERCHANT_ID=MER_test
WAFFO_PRIVATE_KEY="-----BEGIN PRIVATE KEY-----\\nABC\\n-----END PRIVATE KEY-----"
export WAFFO_ENVIRONMENT='test'
`);

  assert.equal(values.WAFFO_MERCHANT_ID, "MER_test");
  assert.equal(values.WAFFO_PRIVATE_KEY, "-----BEGIN PRIVATE KEY-----\nABC\n-----END PRIVATE KEY-----");
  assert.equal(values.WAFFO_ENVIRONMENT, "test");
});

test("buildCheckoutParams uses authenticated buyer identity and metadata", () => {
  const params = buildCheckoutParams(
    {
      storeId: "STO_test",
      productId: "PROD_test",
      currency: "USD",
      publicBaseUrl: "http://127.0.0.1:8787",
    },
    "study-mask-installation",
    {
      buyerEmail: "buyer@example.com",
      metadata: { campaign: "test" },
    },
  );

  assert.equal(params.storeId, "STO_test");
  assert.equal(params.productId, "PROD_test");
  assert.equal(params.productType, "onetime");
  assert.equal(params.buyerIdentity, "study-mask-installation");
  assert.equal(params.buyerEmail, "buyer@example.com");
  assert.equal(params.metadata.installationId, "study-mask-installation");
  assert.equal(params.metadata.campaign, "test");
  assert.equal(params.successUrl, "http://127.0.0.1:8787/payment/success");
});
