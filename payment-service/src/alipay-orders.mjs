import fs from "node:fs";
import path from "node:path";
import { randomBytes, randomUUID } from "node:crypto";

const MAX_ORDERS_IN_MEMORY = 2000;

export class AlipayOrderStore {
  constructor(filePath) {
    this.filePath = filePath;
    this.orders = new Map();
  }

  init() {
    fs.mkdirSync(path.dirname(this.filePath), { recursive: true });

    if (!fs.existsSync(this.filePath)) {
      fs.writeFileSync(this.filePath, "", "utf8");
      return this;
    }

    const lines = fs.readFileSync(this.filePath, "utf8").split(/\r?\n/);
    for (const line of lines) {
      if (!line.trim()) {
        continue;
      }

      try {
        const order = JSON.parse(line);
        if (order?.outTradeNo) {
          this.orders.set(String(order.outTradeNo), order);
        }
      } catch {
        // Ignore malformed historical lines so the service can still start.
      }
    }

    return this;
  }

  async create({ installationId, amount, subject }) {
    const outTradeNo = `sm${Date.now()}${randomBytes(4).toString("hex")}`;
    const order = {
      outTradeNo,
      installationId,
      amount,
      subject,
      status: "CREATED",
      createdAt: new Date().toISOString(),
    };

    this.orders.set(outTradeNo, order);
    try {
      await fs.promises.appendFile(
        this.filePath,
        `${JSON.stringify(order)}\n`,
        "utf8",
      );
    } catch (error) {
      this.orders.delete(outTradeNo);
      throw error;
    }

    this.trimMemory();
    return order;
  }

  get(outTradeNo) {
    return this.orders.get(String(outTradeNo)) || null;
  }

  listByInstallationId(installationId) {
    return [...this.orders.values()]
      .filter((order) => order.installationId === installationId)
      .slice(-20);
  }

  markPaid(outTradeNo, tradeNo, tradeStatus) {
    const order = this.orders.get(String(outTradeNo));
    if (!order) {
      return null;
    }

    order.status = "PAID";
    order.tradeNo = tradeNo;
    order.tradeStatus = tradeStatus;
    order.paidAt = new Date().toISOString();
    this.orders.set(outTradeNo, order);
    return order;
  }

  trimMemory() {
    if (this.orders.size <= MAX_ORDERS_IN_MEMORY) {
      return;
    }

    const keys = [...this.orders.keys()].sort((left, right) => {
      const a = this.orders.get(left).createdAt;
      const b = this.orders.get(right).createdAt;
      return a < b ? -1 : a > b ? 1 : 0;
    });

    const excess = this.orders.size - MAX_ORDERS_IN_MEMORY;
    for (const key of keys.slice(0, excess)) {
      this.orders.delete(key);
    }
  }
}

export function createOutTradeNo() {
  return `sm${Date.now()}${randomUUID().replace(/-/g, "").slice(0, 12)}`;
}
