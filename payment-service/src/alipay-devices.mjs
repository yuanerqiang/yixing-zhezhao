import fs from "node:fs";
import path from "node:path";

const MAX_DEVICES_IN_MEMORY = 20000;

/**
 * 设备授权存储：按机器唯一标识记录「首次使用时间」与「是否已购买」。
 * 首次使用时间记录在服务端，软件重装/重新下载不会重置试用期。
 */
export class AlipayDeviceStore {
  constructor(filePath) {
    this.filePath = filePath;
    this.devices = new Map();
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
        const device = JSON.parse(line);
        if (device?.machineId) {
          this.devices.set(String(device.machineId), device);
        }
      } catch {
        // Ignore malformed historical lines so the service can still start.
      }
    }

    return this;
  }

  /**
   * 注册设备：返回该设备的授权记录。
   * 已有记录则原样返回（首次使用时间不会被覆盖）。
   * 新设备则记录当前时间作为首次使用时间。
   */
  async register(machineId, firstUseHint = null) {
    const key = String(machineId || "").trim();
    if (!key) {
      throw new Error("machineId is required.");
    }

    const existing = this.devices.get(key);
    if (existing) {
      return existing;
    }

    const now = Date.now();
    const hint = firstUseHint ? Number(firstUseHint) : NaN;
    const firstUseAt = Number.isFinite(hint) && hint > 0 && hint <= now
      ? new Date(hint).toISOString()
      : new Date(now).toISOString();

    const device = {
      machineId: key,
      firstUseAt,
      paid: false,
      paidAt: null,
      outTradeNo: null,
    };

    this.devices.set(key, device);
    try {
      await fs.promises.appendFile(
        this.filePath,
        `${JSON.stringify(device)}\n`,
        "utf8",
      );
    } catch (error) {
      this.devices.delete(key);
      throw error;
    }

    this.trimMemory();
    return device;
  }

  get(machineId) {
    return this.devices.get(String(machineId || "").trim()) || null;
  }

  markPaid(machineId, outTradeNo) {
    const key = String(machineId || "").trim();
    const device = this.devices.get(key);
    if (!device) {
      return null;
    }

    device.paid = true;
    device.paidAt = new Date().toISOString();
    device.outTradeNo = outTradeNo;
    this.devices.set(key, device);
    return device;
  }

  isPaid(machineId) {
    const device = this.get(machineId);
    return Boolean(device?.paid);
  }

  trimMemory() {
    if (this.devices.size <= MAX_DEVICES_IN_MEMORY) {
      return;
    }

    // 内存上限保护：按首次使用时间保留最早的一批记录（磁盘文件不受影响）。
    const keys = [...this.devices.keys()].sort((left, right) => {
      const a = this.devices.get(left).firstUseAt;
      const b = this.devices.get(right).firstUseAt;
      return a < b ? -1 : a > b ? 1 : 0;
    });
    const excess = this.devices.size - MAX_DEVICES_IN_MEMORY;
    for (const key of keys.slice(0, excess)) {
      this.devices.delete(key);
    }
  }
}

/**
 * 计算某台设备的试用/定价状态。
 * @returns {{ firstUseAt: string|null, daysUsed: number, trialActive: boolean, price: number, paid: boolean }}
 */
export function computeTrialState(config, device) {
  if (!device) {
    return {
      firstUseAt: null,
      daysUsed: 0,
      trialActive: true,
      price: config.alipayTrialAmount,
      paid: false,
      trialDays: config.alipayTrialDays,
    };
  }

  const firstUse = Date.parse(device.firstUseAt);
  const daysUsed = Number.isFinite(firstUse)
    ? Math.floor((Date.now() - firstUse) / 86_400_000)
    : 0;

  const trialActive = daysUsed < config.alipayTrialDays;

  return {
    firstUseAt: device.firstUseAt,
    daysUsed,
    trialActive,
    price: trialActive ? config.alipayTrialAmount : config.alipayAmount,
    paid: Boolean(device.paid),
    trialDays: config.alipayTrialDays,
  };
}
