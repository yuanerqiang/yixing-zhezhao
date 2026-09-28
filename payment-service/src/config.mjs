import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const serviceRoot = path.resolve(
  fileURLToPath(new URL("..", import.meta.url)),
);

function trimValue(value) {
  return typeof value === "string" ? value.trim() : "";
}

function unquoteEnvValue(value) {
  if (value.length < 2) {
    return value;
  }

  const quote = value[0];
  if ((quote !== "\"" && quote !== "'") || value[value.length - 1] !== quote) {
    return value;
  }

  const inner = value.slice(1, -1);
  if (quote === "'") {
    return inner;
  }

  return inner
    .replace(/\\n/g, "\n")
    .replace(/\\r/g, "\r")
    .replace(/\\t/g, "\t")
    .replace(/\\"/g, "\"")
    .replace(/\\\\/g, "\\");
}

export function parseEnvFile(contents) {
  const values = {};
  const lines = String(contents ?? "").replace(/^\uFEFF/, "").split(/\r?\n/);

  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) {
      continue;
    }

    const normalized = line.startsWith("export ") ? line.slice(7).trim() : line;
    const separator = normalized.indexOf("=");
    if (separator <= 0) {
      continue;
    }

    const key = normalized.slice(0, separator).trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(key)) {
      continue;
    }

    const rawValue = normalized.slice(separator + 1).trim();
    values[key] = unquoteEnvValue(rawValue);
  }

  return values;
}

export function loadEnvironment(
  environment = process.env,
  filePath = path.join(serviceRoot, ".env"),
) {
  const fileValues = fs.existsSync(filePath)
    ? parseEnvFile(fs.readFileSync(filePath, "utf8"))
    : {};

  return { ...fileValues, ...environment };
}

function parsePositiveInteger(value, fallback, name) {
  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  const parsed = Number.parseInt(String(value), 10);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`${name} must be a positive integer.`);
  }

  return parsed;
}

function parseNonNegativeInteger(value, fallback, name) {
  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  const parsed = Number.parseInt(String(value), 10);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    throw new Error(`${name} must be zero or a positive integer.`);
  }

  return parsed;
}

function readPrivateKey(values) {
  const base64Key = trimValue(values.WAFFO_PRIVATE_KEY_BASE64);
  if (base64Key) {
    return Buffer.from(base64Key, "base64").toString("utf8");
  }

  return trimValue(values.WAFFO_PRIVATE_KEY);
}

function parseAmount(value, fallback, name) {
  const raw = value === undefined || value === null || value === ""
    ? fallback
    : String(value).trim();
  const parsed = Number(raw);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    throw new Error(`${name} must be a positive number.`);
  }
  if (/^\d+\.\d{3,}$/.test(raw)) {
    throw new Error(`${name} must have at most 2 decimal places.`);
  }

  return parsed;
}

const ALIPAY_GATEWAY_TEST = "https://openapi-sandbox.dl.alipaydev.com";
const ALIPAY_GATEWAY_PROD = "https://openapi.alipay.com";

export function loadConfig(environment = process.env) {
  const values = loadEnvironment(environment);
  const environmentName = trimValue(values.WAFFO_ENVIRONMENT || "test")
    .toLowerCase();

  if (environmentName !== "test" && environmentName !== "prod") {
    throw new Error("WAFFO_ENVIRONMENT must be either test or prod.");
  }

  const port = parsePositiveInteger(
    values.WAFFO_PORT,
    8787,
    "WAFFO_PORT",
  );
  if (port > 65535) {
    throw new Error("WAFFO_PORT must be between 1 and 65535.");
  }

  const dataDirValue = trimValue(values.WAFFO_DATA_DIR) || "data";
  const dataDir = path.isAbsolute(dataDirValue)
    ? dataDirValue
    : path.resolve(serviceRoot, dataDirValue);

  const alipayEnvironment = (
    trimValue(values.ALIPAY_ENVIRONMENT) || environmentName
  ).toLowerCase();
  if (alipayEnvironment !== "test" && alipayEnvironment !== "prod") {
    throw new Error("ALIPAY_ENVIRONMENT must be either test or prod.");
  }

  const alipayKeyType = (
    trimValue(values.ALIPAY_KEY_TYPE) || "PKCS8"
  ).toUpperCase();
  if (alipayKeyType !== "PKCS1" && alipayKeyType !== "PKCS8") {
    throw new Error("ALIPAY_KEY_TYPE must be either PKCS1 or PKCS8.");
  }

  return {
    merchantId: trimValue(values.WAFFO_MERCHANT_ID),
    privateKey: readPrivateKey(values),
    storeId: trimValue(values.WAFFO_STORE_ID),
    productId: trimValue(values.WAFFO_ONETIME_PRODUCT_ID),
    trialProductId: trimValue(values.WAFFO_TRIAL_PRODUCT_ID),
    mode: environmentName,
    currency: (trimValue(values.WAFFO_CURRENCY) || "USD").toUpperCase(),
    taxCategory: trimValue(values.WAFFO_TAX_CATEGORY) || "software",
    host: trimValue(values.WAFFO_HOST) || "127.0.0.1",
    port,
    publicBaseUrl: (
      trimValue(values.WAFFO_PUBLIC_BASE_URL)
      || `http://127.0.0.1:${port}`
    ).replace(/\/+$/, ""),
    dataDir,
    buyerIdentity: trimValue(values.WAFFO_BUYER_IDENTITY),
    webhookToleranceMs: parseNonNegativeInteger(
      values.WAFFO_WEBHOOK_TOLERANCE_MS,
      300000,
      "WAFFO_WEBHOOK_TOLERANCE_MS",
    ),
    alipayAppId: trimValue(values.ALIPAY_APP_ID),
    alipayPrivateKey: trimValue(values.ALIPAY_PRIVATE_KEY),
    alipayPublicKey: trimValue(values.ALIPAY_PUBLIC_KEY),
    alipayKeyType,
    alipayEnvironment,
    alipayGateway: (
      trimValue(values.ALIPAY_GATEWAY)
      || (alipayEnvironment === "test"
        ? ALIPAY_GATEWAY_TEST
        : ALIPAY_GATEWAY_PROD)
    ).replace(/\/+$/, ""),
    alipayAmount: parseAmount(values.ALIPAY_AMOUNT, "9.90", "ALIPAY_AMOUNT"),
    alipayTrialAmount: parseAmount(
      values.ALIPAY_TRIAL_AMOUNT,
      "4.00",
      "ALIPAY_TRIAL_AMOUNT",
    ),
    alipayTrialDays: parsePositiveInteger(
      values.ALIPAY_TRIAL_DAYS,
      7,
      "ALIPAY_TRIAL_DAYS",
    ),
    alipaySubject: trimValue(values.ALIPAY_SUBJECT) || "学习遮罩",
  };
}

export function hasApiCredentials(config) {
  return Boolean(
    config.merchantId
    && config.privateKey
    && !config.privateKey.includes("<paste-your-private-key>")
    && config.storeId,
  );
}

export function assertApiConfig(config) {
  if (!config.merchantId) {
    throw new Error("WAFFO_MERCHANT_ID is missing.");
  }

  if (!config.privateKey || config.privateKey.includes("<paste-your-private-key>")) {
    throw new Error("WAFFO_PRIVATE_KEY is missing or still contains the placeholder.");
  }

  if (!config.storeId) {
    throw new Error("WAFFO_STORE_ID is missing.");
  }
}

export function assertCheckoutConfig(config) {
  assertApiConfig(config);

  if (!config.productId) {
    throw new Error(
      "WAFFO_ONETIME_PRODUCT_ID is missing. Run npm run setup:product or copy an existing product ID.",
    );
  }
}

export function hasAlipayCredentials(config) {
  return Boolean(
    config.alipayAppId
    && config.alipayPrivateKey
    && !config.alipayPrivateKey.includes("<paste-your-alipay-private-key>")
    && config.alipayPublicKey
    && !config.alipayPublicKey.includes("<paste-your-alipay-public-key>"),
  );
}

export function assertAlipayConfig(config) {
  if (!config.alipayAppId) {
    throw new Error("ALIPAY_APP_ID is missing.");
  }

  if (
    !config.alipayPrivateKey
    || config.alipayPrivateKey.includes("<paste-your-alipay-private-key>")
  ) {
    throw new Error(
      "ALIPAY_PRIVATE_KEY is missing or still contains the placeholder.",
    );
  }

  if (
    !config.alipayPublicKey
    || config.alipayPublicKey.includes("<paste-your-alipay-public-key>")
  ) {
    throw new Error(
      "ALIPAY_PUBLIC_KEY is missing or still contains the placeholder.",
    );
  }
}

export function updateEnvironmentValue(name, value, filePath = path.join(serviceRoot, ".env")) {
  const line = `${name}=${value}`;
  const existing = fs.existsSync(filePath)
    ? fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, "")
    : "";
  const lines = existing ? existing.split(/\r?\n/) : [];
  let replaced = false;

  const updated = lines.map((rawLine) => {
    const normalized = rawLine.trim();
    if (normalized.startsWith(`${name}=`) || normalized.startsWith(`export ${name}=`)) {
      replaced = true;
      return line;
    }
    return rawLine;
  });

  if (!replaced) {
    if (updated.length && updated[updated.length - 1] !== "") {
      updated.push("");
    }
    updated.push(line);
  }

  fs.writeFileSync(filePath, `${updated.join("\n").replace(/\n+$/, "")}\n`, "utf8");
}
