import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";

export function getInstallationId(config) {
  if (config.buyerIdentity) {
    return config.buyerIdentity;
  }

  fs.mkdirSync(config.dataDir, { recursive: true });
  const filePath = path.join(config.dataDir, "installation.json");

  if (fs.existsSync(filePath)) {
    const stored = JSON.parse(fs.readFileSync(filePath, "utf8"));
    if (stored?.installationId) {
      return String(stored.installationId);
    }
  }

  const installationId = `study-mask-${randomUUID()}`;
  fs.writeFileSync(
    filePath,
    `${JSON.stringify({ installationId }, null, 2)}\n`,
    "utf8",
  );
  return installationId;
}
