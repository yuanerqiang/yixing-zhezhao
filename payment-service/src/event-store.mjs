import fs from "node:fs";
import path from "node:path";

export class EventStore {
  constructor(filePath) {
    this.filePath = filePath;
    this.deliveryIds = new Set();
  }

  init() {
    fs.mkdirSync(path.dirname(this.filePath), { recursive: true });

    if (!fs.existsSync(this.filePath)) {
      fs.writeFileSync(this.filePath, "", "utf8");
      return this;
    }

    for (const line of fs.readFileSync(this.filePath, "utf8").split(/\r?\n/)) {
      if (!line.trim()) {
        continue;
      }

      try {
        const event = JSON.parse(line);
        if (event?.id) {
          this.deliveryIds.add(String(event.id));
        }
      } catch {
        // Ignore malformed historical lines so the service can still start.
      }
    }

    return this;
  }

  async record(event) {
    const deliveryId = String(event?.id ?? "");
    if (!deliveryId) {
      throw new Error("Webhook event is missing its delivery id.");
    }

    if (this.deliveryIds.has(deliveryId)) {
      return false;
    }

    this.deliveryIds.add(deliveryId);
    try {
      await fs.promises.appendFile(
        this.filePath,
        `${JSON.stringify(event)}\n`,
        "utf8",
      );
    } catch (error) {
      this.deliveryIds.delete(deliveryId);
      throw error;
    }

    return true;
  }

  readAll() {
    if (!fs.existsSync(this.filePath)) {
      return [];
    }

    const events = [];
    for (const line of fs.readFileSync(this.filePath, "utf8").split(/\r?\n/)) {
      if (!line.trim()) {
        continue;
      }

      try {
        events.push(JSON.parse(line));
      } catch {
        // Ignore malformed historical lines.
      }
    }
    return events;
  }
}
