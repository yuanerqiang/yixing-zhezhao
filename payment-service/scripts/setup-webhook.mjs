import { parseArgs } from "node:util";
import { assertApiConfig, loadConfig } from "../src/config.mjs";
import { createWaffoClient } from "../src/waffo-client.mjs";

const { values } = parseArgs({
  options: {
    url: { type: "string" },
  },
  allowPositionals: false,
});

const config = loadConfig();
assertApiConfig(config);

const publicBaseUrl = String(values.url || "").replace(/\/+$/, "");
if (!/^https:\/\//i.test(publicBaseUrl)) {
  throw new Error(
    "Webhook URL is required and must use HTTPS. Example: npm run setup:webhook -- --url https://example.ngrok.app/api/webhooks/waffo",
  );
}

const webhookUrl = publicBaseUrl.endsWith("/api/webhooks/waffo")
  ? publicBaseUrl
  : `${publicBaseUrl}/api/webhooks/waffo`;
const events = [
  "order.completed",
  "refund.succeeded",
  "refund.failed",
];

const client = createWaffoClient(config);
const queryResult = await client.graphql.query({
  query: `query ($id: String!) {
    store(id: $id) {
      id
      webhookSettings {
        testWebhookUrl
        prodWebhookUrl
        testEvents
        prodEvents
      }
    }
  }`,
  variables: { id: config.storeId },
});

if (queryResult.errors?.length) {
  throw new Error(queryResult.errors.map((error) => error.message).join("; "));
}

const store = queryResult.data?.store;
const current = store?.webhookSettings || {
  testWebhookUrl: null,
  prodWebhookUrl: null,
  testEvents: [],
  prodEvents: [],
};

const webhookSettings = {
  testWebhookUrl: config.mode === "test" ? webhookUrl : current.testWebhookUrl,
  prodWebhookUrl: config.mode === "prod" ? webhookUrl : current.prodWebhookUrl,
  testEvents: config.mode === "test" ? events : current.testEvents,
  prodEvents: config.mode === "prod" ? events : current.prodEvents,
};

const result = await client.stores.update({
  id: config.storeId,
  webhookSettings,
});

console.log(JSON.stringify(result.store.webhookSettings, null, 2));
