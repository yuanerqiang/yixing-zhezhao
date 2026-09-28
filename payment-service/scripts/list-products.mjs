import { assertApiConfig, loadConfig } from "../src/config.mjs";
import { createWaffoClient } from "../src/waffo-client.mjs";

const config = loadConfig();
assertApiConfig(config);

const client = createWaffoClient(config);
const result = await client.graphql.query({
  query: `query ($merchantId: String!) {
    merchant(id: $merchantId) {
      id
      name
      status
      apiKeys {
        nickname
        environment
        recentlyUsed
      }
      storeMerchants {
        role
        store {
          id
          name
          status
        }
      }
    }
    stores {
      id
      name
      status
      onetimeProducts {
        id
        name
        status
      }
    }
  }`,
  variables: {
    merchantId: config.merchantId,
  },
});

if (result.errors?.length) {
  throw new Error(result.errors.map((error) => error.message).join("; "));
}

console.log(JSON.stringify(result.data ?? result, null, 2));
