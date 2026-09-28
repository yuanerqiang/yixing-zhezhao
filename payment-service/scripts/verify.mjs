import { assertApiConfig, loadConfig } from "../src/config.mjs";
import { createWaffoClient } from "../src/waffo-client.mjs";

const config = loadConfig();
assertApiConfig(config);

const client = createWaffoClient(config);
const result = await client.graphql.query({
  query: `query ($storeId: String!, $productId: String!) {
    store(id: $storeId) {
      id
      name
      status
      onetimeProducts {
        id
        name
        status
      }
    }
    onetimeProduct(id: $productId) {
      id
      name
      status
    }
    onetimeProductVersions(productId: $productId) {
      id
      versionNumber
      isTestVersion
      isProdVersion
    }
  }`,
  variables: {
    storeId: config.storeId,
    productId: config.productId,
  },
});

if (result.errors?.length) {
  throw new Error(result.errors.map((error) => error.message).join("; "));
}

console.log(JSON.stringify(result.data ?? result, null, 2));
