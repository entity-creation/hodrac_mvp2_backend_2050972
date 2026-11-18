# Paystack API notes (Hodrac Host)

What `Services/Payments/PaystackClient.cs` relies on, checked on 2026-11-16.

**Source.** The paystack.com/docs pages returned HTTP 403 to automated fetching, so the field names below come from **Paystack's official OpenAPI spec**: [PaystackOSS/openapi `dist/paystack.yaml`](https://github.com/PaystackOSS/openapi/blob/main/dist/paystack.yaml). The human-readable pages say the same things and are worth reading before go-live:
- [Transactions](https://paystack.com/docs/api/transaction/)
- [Subaccounts](https://paystack.com/docs/api/subaccount/)
- [Miscellaneous (List Banks)](https://paystack.com/docs/api/miscellaneous/)
- [Verification (Resolve Account)](https://paystack.com/docs/api/verification/)
- [Refunds](https://paystack.com/docs/api/refund/)
- [Split payments](https://paystack.com/docs/payments/split-payments/)
- [Webhooks](https://paystack.com/docs/payments/webhooks/)

**Every response** is the envelope `{ "status": bool, "message": string, "data": ... }`. The client throws `PaystackException` with Paystack's `message` when `status` is `false` or the HTTP status isn't 2xx. All requests send `Authorization: Bearer <secret key>`.

| Call | Confirmed fields | Notes |
| --- | --- | --- |
| `POST /transaction/initialize` | Body: `email`, `amount` (integer, smallest unit, so **kobo**), `currency`, `reference` (only `-`, `.`, `=` and alphanumerics), `callback_url`, `subaccount` (code), `transaction_charge`, `bearer`, `metadata` (object), `channels`, `split_code`. Response `data`: `authorization_url`, `access_code`, `reference`. | `bearer` is an enum of `account` or `subaccount`; we send `subaccount`. `transaction_charge` is described as "a flat fee to charge the subaccount for a transaction. This overrides the split percentage set when the subaccount was created". The OpenAPI file types it as a string; Paystack's own examples send an integer, and we send an integer in kobo. Our `PaymentReference` format `HDR-yyMMdd-XXXXXXXX` uses only allowed characters. |
| `GET /transaction/verify/{reference}` | `data`: `id`, `status` (`"success"` when paid), `reference`, `amount`, `currency`, `channel`, `paid_at`, `fees`, `fees_split`, `gateway_response`, `metadata` (object **or string**), `subaccount` (object with `subaccount_code`), `authorization`, `customer`. | The client parses `metadata` either way and keeps the raw JSON for the audit column. |
| `POST /subaccount` | Required: `business_name`, `settlement_bank` (bank **code**), `account_number`, `percentage_charge`. Optional: `description`, `primary_contact_email`, `primary_contact_name`, `primary_contact_phone`, `metadata` (stringified JSON). Response `data`: `subaccount_code`, `business_name`, `account_name`, `account_number`, `settlement_bank` (bank **name**), `percentage_charge`, `active`, `id`, … | We send `percentage_charge: 0`. The response echoes `account_number`; the client doesn't map it, so it can't be stored by accident. |
| `PUT /subaccount/{id_or_code}` | Same fields, all optional, plus `active`. | We send only the fields that change, plus `percentage_charge: 0`. |
| `GET /bank` | Query: `country` (`nigeria`), `currency`, `use_cursor`, `perPage`, `next`, `previous`, `pay_with_bank_transfer`, `type`, …. Bank: `name`, `slug`, `code`, `longcode`, `active`, `is_deleted`, `country`, `currency`, `type`, `id`. | Nigeria has more banks than one page holds, so the client pages with `use_cursor=true&perPage=100`, following `meta.next`. It skips inactive and deleted banks, then caches the list for 24 h. |
| `GET /bank/resolve` | Query: `account_number`, `bank_code`. Response `data`: `account_number`, `account_name`, `bank_id`. | An unresolvable account comes back as a 4xx with `status:false`, which the API maps to 400. |
| `POST /refund` | Body: `transaction` (the **reference**, or the id), `amount` (kobo, optional, at most the original), `currency`, `customer_note`, `merchant_note`. | v1 doesn't use it (refunds stay manual). The client method exists for Phase 3+. |

## Differences from or gaps in the spec
1. **A zero `transaction_charge`** is not addressed in the OpenAPI text. Even if Paystack ignored it and fell back to the subaccount's `percentage_charge`, the result is the same: we always create subaccounts with `percentage_charge: 0`, so the whole amount still settles to the subaccount. **Confirm this in test mode** (spec "Fee rules").
2. **`bearer` values.** The OpenAPI spec lists only `account` and `subaccount`. The split-payments guide also mentions `all` and `all-proportional` for multi-split (`split_code`) payments, which we don't use.
3. **Webhooks:** the event names were confirmed in Phase 4 (see "Webhooks" below). The dispute payload shape is still unconfirmed.
4. **`settlement_bank`.** The request takes the bank **code**, but the response returns the bank **name**. We store our own code and name, taken from `/bank`.
5. **Split refunds.** How a refund on a split transaction draws from the main account versus the subaccount is still unconfirmed (spec "Refunds"). Get Paystack's answer in writing before Phase 3 uses `RefundAsync`.

## Webhooks (Phase 4, checked 2026-11-16)

**Source.** paystack.com/docs still returns 403 to automated fetching, and the OpenAPI file couldn't be downloaded from this machine (TLS). The event names below come from Paystack's docs mirror at [docs-v2.paystack.com/payments/webhooks](https://docs-v2.paystack.com/payments/webhooks/) ("Types of events" table). The refund payload comes from [docs-v2.paystack.com/payments/refunds](https://docs-v2.paystack.com/payments/refunds/).

| Event | Confirmed | Reference field | What `PaystackWebhookProcessor` does |
| --- | --- | --- | --- |
| `charge.success` | Yes | `data.reference` | Calls `VerifyAsync(reference)` (never trusts the body), then `PaymentService.ApplyAsync` |
| `refund.pending`, `refund.processing`, `refund.failed` | Yes | `data.transaction_reference` | Stored for the audit only (`refund.failed` is logged) |
| `refund.processed` | Yes | `data.transaction_reference`. In the sample, `data.amount` is a **string** of kobo | Adds the amount to `SeatPayment.RefundedKobo`. The payment becomes `Refunded` once refunds cover `TotalChargedKobo`, otherwise `PartiallyRefunded`. Then recomputes the interest |
| `charge.dispute.create` | Yes | `data.transaction.reference` (**not confirmed**: no sample payload; falls back to `data.transaction_reference`) | Marks the payment `Disputed`, notifies the host (`DisputeOpened`), and emails the host and admins |
| `charge.dispute.remind` | Yes | as above | Emails the admins |
| `charge.dispute.resolve` | Yes | as above; reads `data.resolution` (**not confirmed**) | `"declined"` (the claim was rejected) puts the payment back to paid. Any other value leaves it `Disputed` and emails the admins to reconcile by hand |

- **Signature:** `x-paystack-signature` is the HMAC-SHA512 of the raw body, keyed with the secret key and compared in constant time. A missing or wrong signature gets 401.
- **Paystack's webhook IPs** (test and live): 52.31.139.75, 52.49.173.169, 52.214.14.220. We don't filter by IP because the signature is the check. You can also allow-list these at the edge.
- **Dedupe:** each event is stored with the SHA-256 of its raw body, and an identical redelivery gets 200 straight away. A different payload for the same charge is still safe, because `ApplyAsync` is idempotent on the reference.
- **Processing** runs inline and always returns 200. If processing fails, the error goes in `PaymentWebhookEvents.Error`. The safety net for a missed or failed `charge.success` is the Phase 6 reconcile job, which verifies `Initialized` payments older than 20 minutes. Failed refund and dispute events show up in the admin webhook audit (Phase 7).
- **Before go-live:** send test-mode refund and dispute events, then confirm where the dispute payload carries the reference and which `resolution` values it uses.
