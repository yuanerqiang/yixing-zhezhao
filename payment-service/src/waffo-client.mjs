import { WaffoPancake } from "@waffo/pancake-ts";
import { assertApiConfig } from "./config.mjs";

export function createWaffoClient(config) {
  assertApiConfig(config);

  return new WaffoPancake({
    merchantId: config.merchantId,
    privateKey: config.privateKey,
  });
}
