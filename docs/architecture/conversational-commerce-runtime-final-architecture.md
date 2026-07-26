# Shopixy Conversational Commerce / AI Runtime — FINAL CANONICAL ARCHITECTURE (Implementation-Backed)

> Discipline: Implementation is the ONLY source of truth. Every claim below is cited to `file:line` in the
> backend (C# under `PointOfSale.API/Services/AI`, `PointOfSale.API/Controllers`, `PointOfSale.Core`,
> `PointOfSale.Infrastructure`) or the storefront SPA (`PointOfSale.Storefront/src`). Prior markdown, comments,
> and naming are treated as secondary and were re-verified in code. Items that could NOT be proven from code are
> explicitly marked `NOT FOUND` rather than fabricated.

---

## Executive Summary

Shopixy's "Commerce AI" runtime is a **read-only, single-turn, context-injected conversational commerce assistant**
for the merchant storefront. It is NOT an agent, NOT a tool-caller, NOT an autonomous shopping assistant, and NOT a
recommendation engine. It is most precisely classified as a **Conversational Commerce Explanation Runtime**: it
assembles a bounded JSON snapshot of authoritative commerce state (cart, products, shipping, order, shipment,
customer) and asks an LLM (via an OpenAI-compatible endpoint, defaulting to OpenRouter / DeepSeek) to *explain* that
state and help the customer move toward a purchase decision — producing at most a non-binding `orderDraft` intent,
never a mutation.

Key implementation facts (all cited below):
- The conversation is **stateless server-side**: no conversation/session/message entity, no SQL/Mongo table, no
  history, no token window, no summarization, no memory. Each `POST /api/storefront/chat` is an independent
  request rebuilt from live commerce state (`StorefrontChatController.cs:121`, `CommerceContextBuilder.BuildAsync`).
- The AI is **strictly read-only**: the system prompt forbids order mutation, external API calls, and data
  invention (`AIPromptManager.cs:65-80`, `StorefrontChatController.cs:22-25`). "Order activation" is a draft only
  (`ChatModels.cs:80-99`); the customer still completes checkout through the normal commerce path.
- Knowledge is **fully prompt-injected** from authoritative sources. There is **no RAG, no embeddings, no vector
  search, no semantic retrieval, no function/tool calling** (grep returned 0 tool/function-call/embedding hits in
  `Services/AI`). The only "knowledge" is the bounded `CommerceContext` snapshot.
- Configuration is **SQL-authoritative** (`StoreConfigs` table) with a read-only projection to Mongo
  (`StorefrontConfigDocument`) consumed by the storefront SPA. Merchant controls are limited to a feature toggle,
  widget style/position, greeting, and personality — **not** model, temperature, tokens, or prompt (those are
  platform-config-only in `CommerceAIOptions`).
- Safety/grounding is implemented as **prompt constraints + response-shape validation + safe-fallback + rate
  limiting + tenant isolation**, not as retrieval grounding, citations, or PII scrubbing.

Verdict: **Conversational Commerce Runtime** (a commerce-aware, read-only explanation assistant over a
prompt-injected context window). It is a **Hybrid** only in the sense that it spans pre-purchase assistance AND
post-purchase order/shipment narration, but it is a single, non-agentic LLM-inference runtime.

---

## Architecture Vision

The runtime's own design intent (stated in code comments, but verified against behavior) is a
**"Router-Ready, Storefront-Safe, Order-Aware"** assistant (`AIPromptManager.cs:48-64`). Its vision is to:
- Answer customer questions using ONLY authoritative commerce context (`CommerceContextBuilder.cs:24-28`).
- Explain products, prices, shipping, and policies without inventing data (`AIPromptManager.cs:101-119`).
- Help customers *activate* an order (intent → draft), never execute it (`AIPromptManager.cs:121-135`).
- After purchase, *narrate* order/shipment events, never mutate them (`AIPromptManager.cs:185-263`).
- Never expose model identity, internals, or system instructions (`AIRouterService.cs:24-28`, `ModelRouter.cs:13-19`).

This is a **constrained explanation layer on top of existing commerce truth**, not a new source of commerce truth.

---

## Runtime Purpose (Evidence)

- Customer-facing conversational assistant embedded in the storefront SPA (`ChatWidget.tsx`, `chatService.ts`).
- Purpose = explain existing commerce state and assist purchase decisions (`AIPromptManager.cs:51-64`).
- Endpoint: `POST /api/storefront/chat` (`StorefrontChatController.cs:64-69`); event tracking:
  `POST /api/storefront/commerce-ai/event` (`StorefrontCommerceAiController.cs:39-56`).
- Admin governance: `GET/POST /api/admin/commerce-ai[/toggle]` (`AdminCommerceAiController.cs:77-222`).

## Runtime Scope

Scope = (a) pre-purchase Q&A + draft intent, (b) post-purchase order/shipment narration, (c) a personality/tone
layer, (d) merchant feature-toggle + widget styling, (e) privacy-safe usage counters. It does NOT span catalog
writes, inventory writes, pricing authority, order lifecycle, payment, or recommendations curation (see below).

## Runtime Owns

- **AI feature flag + widget presentation config** (SQL `StoreConfigs`: `CommerceAiEnabled`,
  `CommerceAiWidgetStyle`, `CommerceAiWidgetPosition`, `CommerceAiAutoGreetingEnabled/Message`,
  `CommerceAiPersonality` — `StoreConfig.cs:143-187`, `add_commerce_ai_columns.sql`,
  `Update_StoreConfigs_CommerceAI.sql`).
- **AI usage counters** (Redis counters `commerce_ai:{tenant}:{event}`, 30-day TTL — `CommerceAiInsightsService.cs:30-60`).
- **The prompt assembly and response-shape contract** (`AIPromptManager.cs`, `ChatModels.cs`, `AIResponseValidator.cs`).
- **The in-flight `CommerceContext` snapshot** assembled per request (`CommerceContextBuilder.cs:58-162`).

## Runtime Does NOT Own (Authoritative Truth Deferred)

- Product data → SQL `ProductVariants` / Mongo `ProductDocument` (read-only via `IProductReadRepository`).
- Pricing/Inventory → checkout/SQL authority (context reads only; `StorefrontChatController.cs:22` READ-ONLY).
- Orders/Payments/Shipping → lifecycle owners (`ICommerceAiInsightsService` is counters only; AI never writes).
- Recommendations → `NOT FOUND` in AI services (no recommendation/suggestion logic exists).
- Customer identity → `OnlineCustomerDocument` (read-only) (`CommerceContextBuilder.cs:478-488`).
- FAQs/CMS/Policies → `PoliciesContext.Returns/Delivery` are `null` (hardcoded-empty) — `CommerceContextBuilder.cs:217-222`.

---

## Architecture Principles (each code-cited)

1. **Single source of AI truth = SQL `StoreConfigs`; AI never owns commerce truth.**
   `StoreConfig.cs:143-187`; toggle writes SQL + publishes `StoreConfigUpdatedEvent`
   (`AdminCommerceAiController.cs:174-190`).
2. **Read-only assistant; no mutations, no external calls, no invention.**
   `AIPromptManager.cs:65-80`; `StorefrontChatController.cs:22-25`.
3. **Context is the ONLY knowledge; assembled per request; AI cannot invent outside it.**
   `CommerceContextBuilder.cs:24-28`; `CommerceContext.cs:7-14`.
4. **Tenant isolation is mandatory; X-Tenant-Id stripped; service resolves tenant.**
   `StorefrontChatController.cs:75-83`; tenant scoping on every query (`CommerceContextBuilder.cs:243-258`).
5. **Fail-closed on disabled / missing tenant / AI failure → safe fallback, never internal error exposed.**
   `StorefrontChatController.cs:77-110,141-160`; `AIRouterService.cs:59-63,243-253`; `SafeResponseBuilder.cs`.
6. **Model identity hidden from customer; uniform customer experience regardless of model.**
   `AIRouterService.cs:24-28`; `ModelRouter.cs:13-19`.
7. **Stateless request = no conversation persistence, no memory.**
   `StorefrontChatController.cs:121` (fresh BuildAsync each call); `NOT FOUND` conversations persistence.
8. **Cost ceilings via tiered rate limiting (per-minute/hour/day/month) on every customer LLM execution path.**
   `AIRateLimiter.cs` (Redis); `AIRouterService.ProcessLegacyAsync`; `ModelRouter.RouteAsync`;
   `CommerceAIOptions.RateLimitPer*` (`ChatModels.cs:298-321`).

---

## Core Discovery: What is "Commerce AI" actually?

| Concept | What implementation shows | Evidence |
|---|---|---|
| Commerce AI | A read-only LLM explanation layer over a prompt-injected commerce snapshot | `AIPromptManager.cs:48-64`, `StorefrontChatController.cs:22` |
| Assistant | Single stateless request/response; no agent loop, no tools | `StorefrontChatController.cs:121-133` |
| Conversation | No persistent conversation; per-request context only | `CommerceContextBuilder.cs:58-162`; no Conversation entity |
| Recommendation | Does NOT exist in this runtime (0 recommendation refs) | grep `recommend` in `Services/AI` → none |
| Product Discovery | Not AI-driven; AI only *explains* products already in the injected context | `CommerceContextBuilder.BuildProductsContextAsync` reads cart + in-view IDs only |
| "Agent" | Not an agent runtime — no planning, no tool use, no action execution | `AIPromptManager.cs:65-80` (MUST NOT execute actions) |

**Conclusion:** These are ONE runtime, NOT multiple/nested/independent. "Recommendation" and "Product Discovery" are
NOT separate AI runtimes here — they are absent as AI capabilities. The runtime is a single Conversational Commerce
Runtime that *consumes* (read-only) product/cart/shipping/order data that other runtimes own.

---

## AI Configuration Runtime (Evidence)

Implemented config surface (SQL `StoreConfigs`, projected read-only to Mongo `StorefrontConfigDocument`):

| Field | Implemented? | Default | Evidence |
|---|---|---|---|
| Enabled toggle | YES (SQL) | false | `StoreConfig.cs:143`, `AdminCommerceAiController.ToggleCommerceAi` |
| Widget style (floating/compact/inline/sales) | Partial — single `CommerceAiWidgetStyle` string (default `minimal`); no enum of types | `minimal` | `StoreConfig.cs:162`, `Update_StoreConfigs_CommerceAI.sql:7` |
| Widget position | YES — `CommerceAiWidgetPosition` string | `bottom-right` | `StoreConfig.cs:169` |
| Theme/Branding | NOT FOUND as AI config (theme is a separate `StoreConfig.Theme`) | — | `StoreConfig.cs:40-46` |
| Greeting message | YES — `CommerceAiAutoGreetingMessage` + enable flag | `""` / false | `StoreConfig.cs:174-180` |
| Assistant name | NOT FOUND | — | grep `assistantName` → none |
| Assistant personality | YES — `CommerceAiPersonality` (`friendly|professional|sales` consumed by prompt) | `friendly` | `StoreConfig.cs:187`, `AIPromptManager` Phase8 (`:454-`) |
| Merchant prompt configuration | NOT FOUND (prompt is hardcoded constant `Phase1Prompt`) | — | `AIPromptManager.cs:48` |
| Conversation limits | NOT FOUND (no conversation exists to limit) | — | — |
| Temperature | Platform config ONLY (`CommerceAIOptions.Temperature=0.3`), not merchant-editable | 0.3 | `ChatModels.cs:272` |
| Model selection | Platform config ONLY (`CommerceAIOptions.Model`/`Models`); merchant cannot pick | `deepseek/deepseek-chat` | `ChatModels.cs:247`, `AdminCommerceAiController.Models` is display-only |
| Token limits | Platform config ONLY (`MaxTokens=1000`) | 1000 | `ChatModels.cs:267` |
| Streaming | NOT FOUND — synchronous `SendAsync` + JSON parse | — | grep `stream|SSE` in `Services/AI` → none |
| Typing indicator | Frontend concern only (`ChatWidget.tsx`); no backend support | — | storefront SPA |
| Conversation history | NOT FOUND (stateless) | — | — |
| Live preview | NOT FOUND | — | — |
| Usage statistics | YES — Redis counters `ChatOpened`, `OrderDraftCreated` | — | `CommerceAiInsightsService.cs`, `CommerceAiEventDto.cs` |

**Verified gaps:** branding/theme, assistant name, merchant prompt, conversation limits, temperature/model/token
editing by merchant, streaming, history, live preview = NOT implemented. The "model picker" in the admin UI is
**display-only** (`CommerceAiInfoResponse.Models` = hardcoded list `DeepSeek R1 / GPT-OSS / Gemma`,
`AdminCommerceAiController.cs:97-102`).

---

## Conversation Runtime (Evidence)

- **Conversation / Session / Message entities:** `NOT FOUND`. No `DbSet`, no migration, no Mongo document for
  conversations. `ConversationId` columns that appear in migrations belong to MassTransit outbox, not this runtime.
- **History / Context / Memory / Summarization / Token Window:** `NOT FOUND` server-side. The AI's "memory" is
  exactly one request's `CommerceContext` (rebuilt live each call). The frontend keeps a transient local message
  list (`ChatWidget.tsx` / `useCommerceChat.ts`) but this is NOT sent to the backend and never influences the LLM
  beyond the current `Message`.
- **Persistence / Expiration / Replay / Recovery:** Conversation has no persistence, so no expiration/replay/
  recovery mechanisms exist. If the LLM fails, the only recovery is a `safe fallback` message
  (`SafeResponseBuilder.cs`, `AIRouterService.CreateFallbackResponse`).
- **Lifetime:** single HTTP request → one `CommerceContext` → one LLM call → one validated `ChatResponse`.

---

## Knowledge Runtime (Evidence)

Where the AI gets knowledge — **100% from the injected `CommerceContext`**, assembled by `CommerceContextBuilder`:

| Source | How injected | Evidence |
|---|---|---|
| SQL `StoreConfigs`/`Tenants` | shop name, currency, country, personality | `CommerceContextBuilder.GetStorefrontConfigAsync:239-259` |
| Mongo `ProductDocument` | products in cart + `ProductIdsInView` (≤ `MaxProductsInContext=20`) | `BuildProductsContextAsync:264-327`, `ChatModels.cs:330` |
| `IShippingMethodService` | shipping methods (≤10) with live cart weight/subtotal | `BuildShippingContextAsync:374-420` |
| Cart (`ICartService`) | order draft items/subtotal/total | `BuildOrderDraftContextAsync:425-461` |
| Mongo `OnlineCustomerDocument` | customer name/email/phone if authenticated | `BuildCustomerContextAsync:466-498` |
| Order/Shipment (post-purchase) | only if `DetectPostPurchaseIntent` matches keywords | `CommerceContextBuilder:77-78,106-155` |
| Policies (returns/delivery) | `null` — hardcoded empty, NOT sourced from CMS | `CommerceContextBuilder:217-222` |

- **RAG / Embeddings / Vector search:** `NOT FOUND` (grep `embedding|vector|RAG|semantic` in `Services/AI` → none).
- **Search/Elasticsearch as AI knowledge:** `NOT FOUND` — AI never calls search; it only sees products already in
  cart or currently in view.
- **CMS/FAQs:** `NOT FOUND` (policies are null placeholders).
- **Hardcoded:** the system prompt itself (`AIPromptManager.cs`), and the keyword-based post-purchase detector
  (`CommerceContextBuilder.DetectPostPurchaseIntent:167-179`, incl. Arabic keywords).

**Conclusion:** Knowledge = live, tenant-scoped, read-only SQL+Mongo snapshot injected as JSON. No retrieval,
no grounding corpus.

---

## Commerce Context Runtime (Evidence)

Available context (`CommerceContext.cs:15-77`):
- `Shop` (name, currency, country, policies[null]) — `CommerceContextBuilder.BuildShopContextAsync:185-226`.
- `Products` (id, name, nameAr, price, currency, inStock, description, variants) — `MapProductToContext:332-368`.
- `Shipping` (methods w/ carrier, price, estimatedDays) — `BuildShippingContextAsync:374-420`.
- `OrderDraft` (cart items, subtotal, total) — `BuildOrderDraftContextAsync:425-461`.
- `Customer` (name/email/phone/location if authenticated) — `BuildCustomerContextAsync:466-498`.
- `Message` (current user text).
- `Order` / `Shipment` (post-purchase only) — `CommerceContextBuilder:140-155`.
- `Historical` (confidence layer, post-purchase) — `HistoricalContext.cs`.

**NOT available:** browsing history, wishlist, promotions/coupons, recommendations, FAQs/CMS policies (null),
store policies text, customer location (null), inventory levels beyond a boolean `InStock`, pricing rules/discounts
logic, order history list (only the single detected order).

---

## Tool Runtime (Evidence)

- **Function/tool calling:** `NOT FOUND`. grep `function_call|tools|tool_calls` in `Services/AI` → 0 hits. The LLM
  is called with a plain `{system, user}` chat-completion request and `response_format: json_object`
  (`AIRouterService.ProcessLegacyAsync:99-110`).
- The AI therefore **cannot** Search Products, Get Product, call Inventory/Price/Shipping/Order/Cart/Wishlist
  tools. It can only *describe* whatever the backend pre-injected. The prompt even forbids "Call external APIs"
  (`AIPromptManager.cs:71`).
- The only "interactive" output is a non-binding `orderDraft` intent that the frontend renders as a pre-filled
  checkout suggestion — the human still completes the real order via the commerce checkout path
  (`ChatModels.cs:80-99`, `AIPromptManager.cs:121-135`).

---

## Recommendation Runtime (Evidence)

- **Does not exist as an AI capability.** Zero references to related/similar/cross-sell/up-sell/trending/featured/
  new-arrivals/best-seller/personalized in `PointOfSale.API/Services/AI` (grep `recommend` → none).
- The AI may mention products ONLY if they are already in the injected `Products` context (cart + in-view)
  (`CommerceContextBuilder.BuildProductsContextAsync`). It cannot generate or fetch recommendations.

---

## Product Discovery Runtime (Evidence)

- **Semantic / keyword / vector / hybrid search as an AI function:** `NOT FOUND`. The assistant does not perform
  search. Product Discovery in Shopixy is a separate Elasticsearch/Mongo runtime owned by the storefront/search
  runtime; the AI only *explains* products handed to it.
- The AI's product knowledge is limited to ≤20 products from cart + current page (`ChatModels.cs:330`,
  `CommerceContextBuilder:300-313`).

---

## LLM Runtime (Evidence)

- **Provider:** OpenAI-compatible chat-completions endpoint. Default `Provider="openrouter"`,
  `Endpoint="https://openrouter.ai/api/v1/chat/completions"`, `Model="deepseek/deepseek-chat"`
  (`ChatModels.cs:242-252`). The prompt names "DeepSeek, GPT, Gemma" as possible backends
  (`AIPromptManager.cs:74`). **No Azure/Anthropic/Gemini/Ollama-specific code** — any OpenAI-compatible endpoint
  works via `HttpClient` (`AIRouterService:31,116-118`).
- **Model path — IMPORTANT:** `AIRouterService.ProcessAsync` only invokes `IModelRouter` when
  `_options.Models.Count > 0` (`AIRouterService.cs:66`). The default `appsettings.json` CommerceAI section has
  **no `Models` subsection**, so production traffic runs the **legacy single-model path**
  (`ProcessLegacyAsync`, `AIRouterService.cs:73-192`). The `ModelRouter` + `ModelAdapters` (DeepSeek R1 P1 →
  GPT-OSS P2 → Gemma P3, with `MaxRetries=2` and per-model timeouts 30/25/15s) are **registered but dormant**
  unless an operator adds a `Models` config (`ModelRouter.cs:69-97`, `ModelAdapters.cs:17,53,89`).
- **Default model string:** constant `deepseek/deepseek-chat` (`ChatModels.cs:247`) is overridden at runtime by
  `appsettings.json` `Model=deepseek/deepseek-v3.2` (`appsettings.json:320`). Staging/Test set `Model="none"` +
  empty endpoint → effectively disabled (`appsettings.Staging.json:204`, `appsettings.Test.json:206`).
- **Admin model list mismatch:** the admin UI hardcodes "DeepSeek R1 / GPT-OSS / Gemma"
  (`AdminCommerceAiController.cs:97-102`), which does NOT match the actually-configured `deepseek/deepseek-v3.2` —
  further evidence it is display-only.
- **Streaming:** `NOT FOUND` — synchronous request + JSON parse (`AIRouterService.ParseAIResponse:198-237`).
- **Retry/Timeout:** `TimeoutSeconds` (default 30) via `CancellationTokenSource` (`AIRouterService:128-131`);
  router-level retry across models (`ModelRouter.cs`).
- **Rate limiting:** Redis sliding-window per tenant, tiered minute/hour/day/month (`AIRateLimiter.cs`,
  `ChatModels.cs:298-321`). Fail-open if Redis unavailable (`AIRateLimiter.cs:31-37`).
- **Caching:** `EnableCaching=false` by default; no caching implementation path exercised (`ChatModels.cs:344-349`).
- **Model selection:** platform config only; merchant cannot choose (`CommerceAIOptions.Models`,
  `AdminCommerceAiInfoResponse.Models` is display-only).
- **Prompt building:** `AIPromptManager.GetSystemPrompt()` (concatenated phase prompts) +
  `BuildUserPrompt(context)` (serialized `CommerceContext` JSON) (`AIRouterService:88-89`).

---

## Prompt Runtime (Evidence)

- **System prompt:** hardcoded constant, phase-concatenated (Phase1 pre-purchase + Phase3 post-purchase +
  Phase4 admin + Phase5 historical + Phase8 personality) (`AIPromptManager.cs:48-449+`). Merchant cannot edit.
- **Developer/merchant/store prompt:** `NOT FOUND` as editable inputs. The "store prompt" is the injected
  `CommerceContext` JSON only.
- **Context injection:** `BuildUserPrompt(CommerceContext)` serializes the whole `CommerceContext` to JSON
  (`AIRouterService:89`).
- **Safety prompt:** embedded in system prompt — "NEVER invent data / NEVER execute actions / NEVER expose
  internals" (`AIPromptManager.cs:65-80`).
- **Language prompt:** "Detect customer language automatically (Arabic/English); respond in same language"
  (`AIPromptManager.cs:83-88`); frontend also detects via regex (`chatService.ts:detectLanguage`).
- **Personality prompt:** Phase8 tone-only layer (`friendly|professional|sales`) from `CommerceAiPersonality`
  (`AIPromptManager` Phase8, `CommerceContextBuilder:125-138`).

---

## Runtime Authority (Evidence)

| Concern | Authoritative owner | AI role |
|---|---|---|
| Product information | Catalog/SQL+Mongo | Read-only explainer |
| Pricing | SQL authority / checkout | Read-only explainer |
| Inventory | SQL `ProductVariants` | Read-only (`InStock` boolean only) |
| Shipping | `IShippingMethodService` | Read-only explainer |
| Policies | CMS/StoreConfig | `null` placeholder — NOT provided |
| Recommendations | (separate runtime / absent) | Does not own |
| FAQs | CMS | `NOT FOUND` |
| CMS | CMS runtime | `NOT FOUND` |
| Orders | Order lifecycle owner | Read-only narrator (post-purchase) |
| Customer info | Identity/`OnlineCustomerDocument` | Read-only |
| Store identity | `StoreConfigs` | Read-only reader |
| AI responses | **This runtime** (prompt + validator) | Owns response SHAPE only |

**Does AI own truth?** NO. AI only *explains* truth. The system prompt is explicit: "You do not decide for them.
You do not execute actions. You do not guess." (`AIPromptManager.cs:175-179`).

---

## Runtime Authority Divergence (Evidence)

- **AI ≠ Commerce Truth:** AI output is a generated explanation, not authoritative. Grounding is enforced only by
  prompt ("use ONLY provided context") + the fact that context is the only data the model sees
  (`CommerceContextBuilder.cs:24-28`). There is no citation/evidence check.
- **AI ≠ Inventory:** AI sees `InStock` boolean only, not quantities/reservations (`ProductContext.InStock`
  `CommerceContext.cs:129-130`).
- **AI ≠ Pricing:** AI sees `EffectivePrice`/`OnlinePrice` snapshot, not discount logic
  (`MapProductToContext:339`).
- **AI ≠ Orders:** post-purchase AI *narrates* `OrderTrackingContext`/`ShipmentTrackingContext`; it cannot change
  status (`AIPromptManager.cs:209-214`).
- **Recommendation ≠ Availability:** moot — no recommendations.
- **Search ≠ AI:** search is a separate runtime; AI doesn't search.
- **Prompt ≠ Knowledge:** the prompt lists fields that MAY be present; if absent the AI is told to ask
  (`AIPromptManager.cs:90-99`).
- **LLM ≠ Truth:** every response is validated for shape only (`AIResponseValidator.cs`); factual accuracy is not
  verified — only that it parses to `ChatResponse`.
- **Conversation ≠ Customer Identity:** unauthenticated chat has `Customer.IsAuthenticated=false` and no identity
  linkage (`CustomerContext.cs:273-274`, `StorefrontChatController.cs:112-118`).

---

## Grounding Runtime (Evidence)

- **Grounding / Citation / Evidence / Verification / Hallucination Prevention:** implemented ONLY as (a) the
  prompt's "use ONLY provided context / NEVER invent" rule (`AIPromptManager.cs:65-80,101-119`) and (b) the
  per-request context rebuild that gives the model no other data (`CommerceContextBuilder.cs:24-28`). There is NO
  retrieval grounding, NO citation mechanism, NO factual verification against source after generation.
- **Confidence:** a *communicative* confidence layer for legacy orders only (`ConfidenceResolver.cs`,
  `HistoricalContext.cs`) — it tells the AI how to *phrase* uncertainty about old orders, not whether the answer is
  true. `Confidence` enum is `[JsonIgnore]` and never reaches the model; only `ConfidenceNote` text does
  (`HistoricalContext.cs:96-106`).
- **Fallback / Unknown Answer / Tool Validation:** fallback message on any failure/validation miss
  (`SafeResponseBuilder.cs`, `AIResponseValidator.cs`). No "I don't know" tool — the prompt supplies a canned
  fallback phrase (`AIPromptManager.cs:164-173`).
- **Real grounding check (strongest mechanism present):** `AIResponseValidator.TryValidate` rejects the ENTIRE
  response if an `orderDraft.items[].productId` is not present in `context.Products`, or if `shippingMethodId`
  is not in `context.Shipping.Methods`; quantities are clamped to ≥1 (`AIResponseValidator.cs:102-151`). This is a
  genuine grounding guard — the AI cannot draft an order referencing products/shipping the runtime did not load.
- **Verdict:** Grounding = context-confinement (prompt) + bounded context (only cart + in-view products) + a
  concrete response validator that rejects out-of-context product/shipping IDs. NOT retrieval/citation-grounded
  (no source attribution, no post-generation factual verification against source systems).

---

## Memory Runtime (Evidence)

- **Short/Long/Customer/Store/Vector/Session/Persistent Memory:** `NOT FOUND`. The runtime is stateless. The only
  "memory" is the in-request `CommerceContext`. `Redis` is used for rate-limit counters and usage counters — NOT for
  conversation memory (`AIRateLimiter.cs`, `CommerceAiInsightsService.cs`).

---

## Personalization Runtime (Evidence)

- **Language:** auto-detected Arabic/English in prompt + frontend (`AIPromptManager.cs:83-88`,
  `chatService.ts:detectLanguage`). `ChatRequest.Language` may be supplied (`ChatModels.cs:38`).
- **Customer/Guest/Returning:** `Customer.IsAuthenticated` only (`CustomerContext.cs:273-274`); no returning-
  customer personalization beyond name/email read from `OnlineCustomerDocument`.
- **Cart/Wishlist/Browsing/Purchase History/Location:** cart yes (read-only); wishlist `NOT FOUND`; browsing
  `NOT FOUND`; purchase history `NOT FOUND` (only the single detected post-purchase order); location `null`.
- **Store preferences / personality:** `CommerceAiPersonality` (friendly/professional/sales) from `StoreConfigs`
  (`CommerceContextBuilder:125-138`).

---

## Safety Runtime (Evidence)

- **Prompt injection protection:** partial. The model is told not to follow instructions outside context
  (`AIPromptManager.cs:65-80`), and only the serialized `CommerceContext` is injected as the user turn — but the
  customer `Message` is concatenated raw into the user prompt (`AIRouterService.BuildUserPrompt` via
  `CommerceContext.Message`). There is NO input sanitization of the customer message before prompt assembly.
- **Prompt sanitization:** `NOT FOUND` (no sanitizer on `Message`).
- **Tool permission:** N/A — no tools.
- **Rate limiting:** YES, Redis tiered (`AIRateLimiter.cs`).
- **Content filtering:** `NOT FOUND` beyond the JSON-shape validator (`AIResponseValidator.cs`) and prompt
  constraints. No toxicity/PII filter on output.
- **Hallucination guard:** prompt-only ("never invent") + shape validation; no factual check.
- **Tenant isolation:** YES — tenant resolved by middleware, every query scoped (`StorefrontChatController.cs:75-83`,
  `CommerceContextBuilder` tenant-scoped queries).
- **Secret protection:** API key from `CommerceAIOptions.ApiKey` (config), never exposed; model identity hidden
  (`AIRouterService.cs:24-28`).
- **PII protection:** partial — customer PII is read from `OnlineCustomerDocument` and injected into context only
  when authenticated; no scrubbing of PII that the model might echo.

---

## AI Event Runtime (Evidence)

- **Insight counters accepted:** `CommerceAiEventType` enum has only `ChatOpened` and `OrderDraftCreated`
  (`CommerceAiEventDto.cs`). Frontend posts to `/api/storefront/commerce-ai/event`
  (`StorefrontCommerceAiController.TrackEvent:43-56`), which fire-and-forgets a Redis increment
  (`CommerceAiInsightsService.TrackEventAsync`).
- **Conversation Started / Message Sent / Message Received / Tool Called / Tool Failed / Recommendation Clicked /
  Purchase Assisted / Conversion Assisted:** `NOT FOUND` as AI events. Only `ChatOpened` + `OrderDraftCreated`
  counters exist.
- **Outbox/publisher:** the AI event endpoint does NOT use the generic outbox; it writes Redis directly and returns
  202 Accepted (`StorefrontCommerceAiController.cs:53-55`). The admin toggle DOES publish `StoreConfigUpdatedEvent`
  via `IEventPublisher` (`AdminCommerceAiController.cs:179-187`) — that is a config event, not an AI conversation
  event.

---

- **Business lifecycle semantics:** `OrderDraftCreated` is a storefront insight signal that a draft intent was
  surfaced or clicked; it is not checkout admission, order creation, purchase assistance, conversion attribution,
  or any authoritative order lifecycle event.

## Commerce Runtime Integration (Evidence)

Read-only consumption of peer runtimes (all cited above):
- **Identity:** reads `CustomerId` claim + `OnlineCustomerDocument` (`StorefrontChatController.cs:112-118`,
  `CommerceContextBuilder:466-498`).
- **Storefront:** reads `StoreConfigs`/`StorefrontConfigDocument` for widget + personality
  (`CommerceContextBuilder:239-259`, projection `StorefrontConfigProjectionConsumer.cs:576-582`).
- **Catalog:** reads Mongo `ProductDocument` (cart + in-view) (`CommerceContextBuilder:311-318`).
- **Inventory:** reads `InStock` boolean only (`ProductContext.InStock`).
- **Pricing:** reads `EffectivePrice`/`OnlinePrice` (`MapProductToContext:339,360`).
- **Search:** NOT consumed by AI.
- **Cart:** reads via `ICartService` (`CommerceContextBuilder:425-461`).
- **Checkout/Payment/Order/Shipping:** post-purchase narrates `OrderTrackingContext`/`ShipmentTrackingContext`;
  writes NONE.
- **CMS/FAQ/Policies:** `null` placeholder (`CommerceContextBuilder:217-222`).
- **Tracking/Analytics:** AI usage counters to Redis; no GA4/Meta integration from AI.
- **Affiliate/CommerceHub/Notifications:** `NOT FOUND` in AI services.

---

## Runtime State Machines (Evidence)

- **Assistant (feature) state machine:** `CommerceAiEnabled` boolean on `StoreConfigs`; transitions
  Enabled↔Disabled by admin (optimistic-concurrency guarded via `If-Match`/`RowVersion`,
  `AdminCommerceAiController.cs:147-199`). Events: `StoreConfigUpdatedEvent` on change.
- **Chat request state machine (per call):** CheckEnabled → BuildContext → RateLimit → Route(LLM) →
  Validate → Respond | Fallback. No persisted states.
- **Model routing state machine:** attempt adapters by priority up to `MaxRetries`; on all-fail → safe fallback
  (`ModelRouter.cs:80-140`).
- **Order-draft intent states:** `none|create|update|confirm` (`ChatModels.cs:145-158`) — intent only, never
  persisted.
- **Response type states:** `message|question|order_draft` (`ChatModels.cs:128-139`).
- **Historical confidence states:** `High|Medium|Low` (`HistoricalContext.cs:26-45`) — communicative only.
- **Conversation/session/tool/recommendation/streaming/retry/recovery state machines:** `NOT FOUND` (no
  conversation/tool/recommendation/streaming persistence).

---

## Runtime Authority Matrix (Evidence)

| Concern | Owner (writes) | AI reads? | AI writes? | AI runtime truth? |
|---|---|---|---|---|
| Conversation | NONE (stateless) | n/a | n/a | No |
| Knowledge | Catalog/Pricing/Inventory/Order | Yes (injected) | No | No |
| Product data | Catalog | Yes | No | No |
| Pricing | SQL/Checkout | Yes (snapshot) | No | No |
| Inventory | SQL | Yes (bool) | No | No |
| Policies | CMS | No (null) | No | No |
| Recommendations | Absent | n/a | n/a | No |
| Customer | Identity | Yes (read-only) | No | No |
| Orders | Order lifecycle | Yes (narrate) | No | No |
| Shipping | Shipping svc | Yes (read-only) | No | No |
| AI Response | This runtime | n/a | Yes (shape) | Yes (response only) |
| Prompt | This runtime (hardcoded) | n/a | Yes (constant) | Yes (prompt only) |
| Memory | NONE | n/a | n/a | No |

---

## CQRS (Evidence)

- **Write models:** AI runtime writes only `StoreConfigs` (toggle) and Redis counters. No domain write models.
- **Read models:** `CommerceContext` assembled from SQL (`StoreConfigs`/`Tenants`/`Carts`) + Mongo
  (`ProductDocument`/`OnlineCustomerDocument`) + `IShippingMethodService` (`CommerceContextBuilder.cs`).
- **Redis:** usage + rate-limit counters only (`CommerceAiInsightsService.cs`, `AIRateLimiter.cs`). NOT a
  conversation/context read model.
- **Mongo:** reads product/customer docs; writes `StorefrontConfigDocument` (config projection, not AI-authored).
- **Outbox:** generic `OutboxMessage` used by admin toggle's `StoreConfigUpdatedEvent`
  (`AdminCommerceAiController.cs:179-187`); AI insight counters intentionally remain Redis direct and
  non-authoritative.
- **Replay/Repair/Projection:** config projection `StorefrontConfigProjectionConsumer.cs:576-582` (SQL→Mongo).
  Conversation has no projection (stateless).

---

## Snapshot Runtime (Evidence)

- The only "snapshot" is the per-request immutable `CommerceContext` JSON handed to the LLM
  (`CommerceContext.cs`, `AIRouterService:89`). It is NOT persisted (no snapshot store). There is no immutable
  snapshot of conversation/prompt/knowledge/recommendation/customer commerce context beyond this in-flight object.

---

## Cross Runtime Authority Graph (Evidence)

Peer runtimes and the AI's relationship (read-only consumer unless noted):

| Peer | Reads | Writes | Authority |
|---|---|---|---|
| Identity | customer claim + doc | none | consumer |
| Storefront | config/doc | none | consumer |
| CMS | none (policies null) | none | absent |
| Catalog | product doc | none | consumer |
| Search | none | none | absent |
| Inventory | InStock bool | none | consumer |
| Pricing | price snapshot | none | consumer |
| Tracking/Analytics | none | Redis counters | producer (counters only) |
| Cart | cart items | none | consumer |
| Checkout | none | none | absent (AI draft only) |
| Payment | none | none | absent |
| Order | order/shipment context | none | consumer (narrate) |
| Shipping | methods | none | consumer |
| Reporting | none | none | absent |
| Affiliate | none | none | absent |
| CommerceHub | none | none | absent |
| Notifications | none | none | absent |
| Builder | config (via StoreConfig) | none | consumer |

---

## Business Invariants (Implementation-Backed)

1. AI MUST be disabled by default and only enabled by an authenticated Owner/Admin
   (`StoreConfig.cs:143` default false; `AdminCommerceAiController.cs:48` `[Authorize(Roles="Owner,Admin")]`).
2. AI MUST return 403 when disabled (`StorefrontChatController.cs:99-110`).
3. AI MUST never mutate orders/payments/shipments (`AIPromptManager.cs:65-80,209-214`; `StorefrontChatController.cs:22`).
4. AI MUST use only injected context and never invent (`CommerceContextBuilder.cs:24-28`; `AIPromptManager.cs:68-77`).
5. AI MUST hide model identity (`AIRouterService.cs:24-28`; `ModelRouter.cs:13-19`).
6. AI MUST validate response shape before returning (`AIResponseValidator.cs`; `AIRouterService:162`).
7. AI MUST fail closed to a safe fallback on any error/timeout/invalid
   (`AIRouterService.CreateFallbackResponse`; `SafeResponseBuilder.cs`).
8. AI MUST be tenant-scoped (`StorefrontChatController.cs:75-83`; tenant-scoped queries throughout).
9. Config changes MUST be optimistic-concurrency guarded (`If-Match`/`RowVersion`)
   (`AdminCommerceAiController.cs:147-199`).
10. Usage MUST respect cost ceilings (per-minute/hour/day/month) on legacy and multi-model customer routes
    (`AIRateLimiter.cs`, `AIRouterService.ProcessLegacyAsync`, `ModelRouter.RouteAsync`, `ChatModels.cs:298-321`);
    provider responses are recorded before response-shape validation so invalid LLM output cannot bypass usage
    accounting.

---

## Architectural Decisions (WHY — Implementation Evidence)

- **Stateless by design:** simplest path to tenant isolation + no PII retention risk; conversations need no
  recovery. Evidence: no conversation store; fresh `BuildAsync` per call.
- **Prompt-injection-of-context (not RAG):** the commerce dataset is small/structured per request (cart + in-view),
  so a bounded JSON context suffices and avoids vector infra. Evidence: `CommerceContextBuilder` assemblies only
  cart + ≤20 in-view products.
- **Read-only assistant:** aligns with Shopixy's authoritative-truth rule (AI explains, lifecycle owners execute).
  Evidence: system prompt + controller READ-ONLY comment.
- **OpenAI-compatible single endpoint + model router:** provider-portable, no SDK lock-in; DeepSeek/OpenRouter
  default for cost. Evidence: `HttpClient` + `ModelRouter` adapters.
- **Redis for rate-limit + counters:** cheap, ephemeral, fail-open safe. Evidence: `AIRateLimiter.cs`,
  `CommerceAiInsightsService.cs`.
- **SQL-authoritative config + Mongo projection:** consistent with the rest of the merchant platform
  (StorefrontConfig). Evidence: `StoreConfig.cs` + `StorefrontConfigProjectionConsumer.cs:576-582`.
- **Hardcoded prompt + display-only model picker:** keeps merchants from breaking safety constraints; model choice
  stays platform-operational. Evidence: `AIPromptManager` constant; `AdminCommerceAiInfoResponse.Models` hardcoded.

---

## Technical Debt (Implementation-Backed)

1. **No conversation persistence / memory** — multi-turn coherence relies solely on the latest `Message`; the
   model has no prior turns. (`StorefrontChatController.cs:121`; no Conversation entity.)
2. **No real grounding/citation** — factual accuracy unverified post-generation; only shape-validated.
   (`AIResponseValidator.cs`.)
3. **Prompt-input hardening is guardrail-only** — customer messages are control-character sanitized and the system
   prompt treats customer/catalog/store text as untrusted data, but no semantic prompt-injection classifier exists
   (`CommerceContextBuilder.SanitizePromptText`, `AIPromptManager.cs`).
4. **Policies/FAQs are null** — the assistant claims to explain policies but receives none
   (`CommerceContextBuilder.cs:217-222`).
5. **No recommendations / discovery in AI** — advertised "Commerce AI" has no recommendation or search capability.
6. **Display-only model picker + no merchant prompt/temperature/model control** — admin UI implies config that
   doesn't exist (`AdminCommerceAiController.cs:97-102`).
7. **No streaming** — synchronous; large contexts may hit `TimeoutSeconds` (default 30).
8. **Rate limiter fail-open on Redis outage** — unlimited AI spend if Redis down (`AIRateLimiter.cs:31-37`).
9. **`DefaultFeatureTimelineProvider` returns all-features-from-beginning** — historical confidence layer is
   effectively a no-op in production (`ConfidenceResolver.cs:171-179`).
10. **`ConfidenceResolver` is dead code on the chat path:** `CommerceContextBuilder` computes confidence via an
    inline heuristic (`BuildHistoricalContextAsync`: order age >180d → Medium; shipment-missing+Delivered → Low;
    etc.) and does NOT inject/use `IConfidenceResolver` (`CommerceContextBuilder.cs:39-55,652-721`). The richer
    registered resolver is unreached.
11. **`OrderContextProvider`/`IOrderContextProvider` registered but unused + stubbed:** `CommerceContextBuilder`
    queries Mongo orders/shipments directly; `OrderContextProvider`'s repo is `StubOrderTrackingRepository`
    returning null for everything (`OrderContextProvider.cs:345-367`, `Program.cs:1553-1554`).
12. **`SessionId` is a cart-owner key, not a conversation key:** it only resolves the cart
    (`CommerceContextBuilder.GetCartAsync`); there is no conversation key at all
    (`ChatModels.cs:23`, `StorefrontChatController.cs:126`).
13. **Admin model list mismatches actual configured model** and the multi-model `ModelRouter` is dormant by
    default (legacy path runs) — see LLM Runtime.
14. **Caching flag present but unwired for the customer path:** `appsettings` sets `EnableCaching`/`CacheDurationSeconds`
   but no cache code exists in the router/adapter path; every request hits the LLM
   (`AIRouterService.cs`, `ModelAdapters.cs`).
15. **Caching disabled by default** — every request hits LLM + context build (`ChatModels.cs:344-349`).
16. **AI insight counters are Redis-only** — `ChatOpened`/`OrderDraftCreated` are non-authoritative usage counters,
    not durable/reconciled business lifecycle events (`StorefrontCommerceAiController.cs:53-55`).

---

## Implementation Follow-up Items

| Item | Priority | Status |
|---|---|---|
| Add semantic prompt-injection classifier beyond control-character sanitization / instruction-boundary guard | Med | Not started |
| Persist conversation or implement sliding context window | High | Not started |
| Wire CMS policies/FAQs into `PoliciesContext` | Med | Not started |
| Add real grounding/citation verification | High | Not started |
| Make model/temperature/prompt merchant-configurable (or remove UI illusion) | Med | Not started |
| Add streaming with timeout hardening | Med | Not started |
| Durable AI events via outbox if counters become business lifecycle evidence | Low | Not started |
| Production `FeatureTimelineProvider` for real confidence | Low | Not started |
| Fail-closed rate limiter on Redis outage | Med | Not started |

---

## Runtime Timeline — One Customer Journey (Evidence per step)

1. **Customer opens storefront** → SPA loads `StorefrontConfigDocument` (incl. `CommerceAiEnabled`)
   (`StorefrontConfigProjectionConsumer.cs:576-582`; `ChatWidget.tsx` renders only if enabled).
   Owner: Storefront/Builder. Authority: SQL→Mongo projection. Sync: event-driven. Failure: widget hidden.
2. **AI widget appears** → `ChatWidget.tsx` conditional on `commerceAiEnabled` (`bootstrap.ts:162`).
   Owner: Storefront SPA. Authority: config read.
3. **Greeting** → if `CommerceAiAutoGreetingEnabled`, `ChatWidget` shows `CommerceAiAutoGreetingMessage`
   (`StorefrontConfigDocument`). Owner: Storefront.
4. **Customer question** → `POST /api/storefront/chat` with `Message`, `ProductIdsInView`, `SessionId`
   (`chatService.ts`, `StorefrontChatController.cs:64`).
5. **Context resolution** → `CommerceContextBuilder.BuildAsync` assembles shop/products/shipping/orderDraft/
   customer (tenant-scoped, parallel) (`CommerceContextBuilder.cs:58-162`). Owner: AI runtime (read-only).
6. **Knowledge resolution** → context IS the knowledge (no retrieval). Owner: peer runtimes (sources).
7. **Tool calling** → NONE. Owner: n/a.
8. **Commerce context injection** → `BuildUserPrompt(context)` serializes `CommerceContext` to JSON
   (`AIRouterService:89`). Owner: AI runtime.
9. **LLM** → `AIRouterService`/`ModelRouter` → OpenAI-compatible endpoint, JSON mode
   (`AIRouterService.ProcessLegacyAsync:99-110`). Owner: external LLM. Grounding: prompt-only.
10. **Response** → `AIResponseValidator.TryValidate` enforces shape (`AIResponseValidator.cs:21`). Consistency:
    shape-only.
11. **Recommendation** → NONE generated. Owner: n/a.
12. **Customer click** → optional `POST /commerce-ai/event` `OrderDraftCreated` increments Redis
    (`StorefrontCommerceAiController.cs:43-56`, `CommerceAiEventDto.cs`).
13. **Add to cart / Checkout / Purchase** → OUTSIDE AI runtime; normal commerce checkout. AI only produced a
    non-binding `orderDraft` intent (`ChatModels.cs:80-99`). Owner: Checkout/Order runtime.
14. **Conversation ends** → stateless; nothing persisted (`StorefrontChatController.cs:121`). Recovery: n/a.

---

## Runtime Classification (Evidence-Backed)

**Classification: Conversational Commerce Runtime (read-only, context-injected explanation assistant).**

Supporting evidence:
- It is fundamentally a **Commerce Runtime** surface (explains cart/products/shipping/orders) — but it is NOT a
  commerce authority (no writes).
- It is a **Conversation Runtime** only in the thin sense of one request/response turn; no persistent conversation.
- It is NOT a **Recommendation Runtime**, **Agent Runtime**, or **Product Discovery Runtime** (those are absent).
- It is NOT an **Assistant Runtime** that acts (it only explains/narrates).
- It is a **Hybrid** only across pre-purchase (assist) + post-purchase (narrate) modes, within one non-agentic LLM
  runtime.

Final answer to the architecture question: Shopixy transforms natural-language customer conversations into accurate,
grounded, commerce-aware assistance by (1) resolving the tenant, (2) assembling a bounded, tenant-scoped,
authoritative `CommerceContext` snapshot from SQL+Mongo+services (read-only), (3) injecting it as the user prompt
with a hardcoded safety system prompt, (4) calling an OpenAI-compatible LLM that may only explain/narrate and emit
a non-binding `orderDraft` intent, (5) validating the response shape, and (6) failing closed to a safe fallback on
any divergence — thereby preserving authoritative business truth because the AI never writes commerce state and can
only describe what the runtime pre-loaded. Grounding is achieved by context-confinement + prompt constraints, NOT by
retrieval/citation (which are NOT implemented).

---

## Certification

- **Confidence:** High on architecture shape, classification, ownership, authority, and the "what is absent" claims
  (verified by direct code reads + grep).
- **Completeness:** Covered all requested sections with implementation evidence.
- **Verified (code-cited):** stateless conversation, read-only, prompt-injected context, OpenAI-compatible/OpenRouter
  default, Redis rate-limit + counters, SQL-authoritative config + Mongo projection, no RAG/tools/streaming/
  recommendations, hardcoded prompt, display-only model picker, safe-fallback, tenant isolation.
- **Missing/absent (explicitly marked NOT FOUND):** conversation persistence, memory, RAG/embeddings, tool/function
  calling, streaming, recommendations, product discovery as AI, CMS/FAQ grounding, merchant prompt/temperature/model
  editing, PII scrubbing, conversation-level events.
- **Debt summary:** see Technical Debt section (11 items). No fabricated capabilities.
