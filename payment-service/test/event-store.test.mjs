import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { EventStore } from "../src/event-store.mjs";

test("EventStore persists events and suppresses duplicate delivery IDs", async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "study-mask-payments-"));
  const filePath = path.join(directory, "events.jsonl");

  try {
    const store = new EventStore(filePath).init();
    const event = {
      id: "delivery-1",
      eventType: "order.completed",
      data: { metadata: { installationId: "install-1" } },
    };

    assert.equal(await store.record(event), true);
    assert.equal(await store.record(event), false);
    assert.deepEqual(store.readAll(), [event]);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
