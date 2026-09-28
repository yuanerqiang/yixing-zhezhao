# Study Mask payment service

This local Node service bundles two payment channels:

- **Waffo Pancake** (`@waffo/pancake-ts`) — overseas card checkout (USD).
- **Alipay 电脑网站支付** (`alipay-sdk`) — Chinese users pay with Alipay (CNY).

The merchant private keys never enter the browser or the compiled WinForms app.

## Setup (Waffo / card channel)

1. Install dependencies:

   ```powershell
   npm install
   ```

2. Copy `.env.example` to `.env` and paste the RSA private key from the Waffo
   dashboard into `WAFFO_PRIVATE_KEY`.

3. Create a test one-time product in the supplied store. Choose the real price
   before running this command:

   ```powershell
   npm run setup:product -- --amount 9.90 --currency USD --write-env
   ```

4. Verify the credentials, store, and product:

   ```powershell
   npm run verify
   ```

5. Start the service:

   ```powershell
   npm start
   ```

The WinForms purchase button asks the user to pick a channel, checks
`http://127.0.0.1:8787/health`, starts the service when needed, then opens:

- `http://127.0.0.1:8787/buy` — Waffo card checkout
- `http://127.0.0.1:8787/alipay/buy` — Alipay cashier

## Setup (Alipay channel)

1. Go to https://open.alipay.com, log in with an **enterprise or
   individual-business (个体工商户) Alipay account**, finish developer
   onboarding, create a **网页&移动应用**, and sign the **电脑网站支付**
   product.

2. Use the Alipay key tool to generate an RSA2 key pair, upload the **应用公钥**
   to the platform, and keep the **应用私钥** (only on your own machine). Copy
   the **支付宝公钥** from the app details page.

3. Fill these values in `payment-service/.env`:

   ```dotenv
   ALIPAY_APP_ID=2021...
   ALIPAY_PRIVATE_KEY="-----BEGIN PRIVATE KEY-----..."
   ALIPAY_PUBLIC_KEY="-----BEGIN PUBLIC KEY-----..."
   ALIPAY_ENVIRONMENT=test        # test = sandbox, prod = live
   ALIPAY_AMOUNT=9.90             # unit price in CNY
   ```

   If your private key starts with `-----BEGIN RSA PRIVATE KEY-----`, set
   `ALIPAY_KEY_TYPE=PKCS1` instead of the default `PKCS8`.

4. Verify the configuration (generates a signed form locally and pings the
   gateway):

   ```powershell
   npm run verify:alipay
   ```

5. Start the service and open the cashier in a browser:

   ```powershell
   npm start
   ```

   Then visit `http://127.0.0.1:8787/alipay/buy`. In sandbox mode, pay with the
   buyer account provided by the sandbox console (no real money).

6. Before going live: set `ALIPAY_ENVIRONMENT=prod`, re-check the software
   category/qualification requirements on the platform, and test a real payment
   with a small amount.

### How Alipay settlement is confirmed

The desktop app runs the payment service on the user's own machine, so Alipay's
server-side asynchronous notify cannot reach it (that would need a public
server). Instead:

1. The local service creates an order and renders Alipay's auto-submitting
   checkout form (`alipay.trade.page.pay`).
2. After payment, Alipay redirects the user's browser back to
   `/payment/alipay/return` on localhost.
3. That page polls `/api/alipay/verify`, which actively queries
   `alipay.trade.query`. Only when Alipay reports `TRADE_SUCCESS` /
   `TRADE_FINISHED` is an `alipay.order.completed` event recorded and the
   entitlement granted.

This is the standard pattern for desktop software; no public server or domain
is required. Orders are kept in `data/alipay-orders.jsonl`.

## Webhooks (Waffo only)

Waffo needs a public HTTPS URL for webhook delivery. During local development,
expose port 8787 with a tunnel that preserves custom headers:

```powershell
ngrok http 8787
npm run setup:webhook -- --url https://your-public-id.ngrok.app
```

The webhook route is:

```text
POST /api/webhooks/waffo
```

It reads the raw request body, verifies `X-Waffo-Signature` with the SDK, and
persists verified events to `data/webhook-events.jsonl`. The delivery ID is
deduplicated before the event is stored.

## Test mode

Waffo test cards (any future expiry and CVC):

- Success: `4576 7500 0000 0110`
- Declined: `4576 7500 0000 0220`

Alipay sandbox: use the buyer account shown in the sandbox console.

## Endpoints

- `GET /health`: configuration and runtime status (both channels).
- `GET /buy`: Waffo authenticated checkout redirect.
- `GET /alipay/buy`: creates an Alipay order and renders the cashier form.
- `POST /api/checkout`: JSON checkout endpoint for other local callers.
- `GET /payment/alipay/return`: Alipay return page (polls verify).
- `GET /api/alipay/verify`: queries Alipay and records the entitlement.
- `GET /api/alipay/orders`: recent Alipay orders for an installation.
- `POST /api/webhooks/waffo`: raw-body Waffo webhook verification.
- `GET /api/events`: recent verified webhook events.
- `GET /api/entitlement`: entitlement derived from verified events.

