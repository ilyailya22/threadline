# Demo video — shot list

The assignment asks for a short video of the running application that shows as much of the
implemented functionality as possible. This is the order that covers every checklist item in about
seven minutes. Record at 1080p; keep the browser at ~1280 px wide so the table is a table.

The narration itself — what to say, word for word, and what to do with the mouse while saying it —
is in [DEMO-SCRIPT.uk.md](DEMO-SCRIPT.uk.md), in the language the video is recorded in.

**Before recording**

```bash
docker compose down -v
docker compose up -d --build
dotnet run --project tools/Comments.Seeder -c Release -- --comments 1000000 --users 100000 --truncate true
```

Have two browser windows side by side (for the live-update shot), a 1920×1080 JPG, a small `.txt`
and a `.txt` over 100 KB ready on the desktop.

---

| # | Time | Show | Say / caption |
|---|---|---|---|
| 1 | 0:00 | Terminal: `docker compose ps` — all healthy | "One command brings up SQL Server, Redis, RabbitMQ, Elasticsearch, Azurite, API, worker and the SPA." |
| 2 | 0:20 | SQL: `SELECT COUNT(*) FROM Comments` → 1,000,000 | "Seeded with the Middle+ volume: a million comments, 100k users." |
| 3 | 0:35 | Main page: table, 25 rows, newest first | "Top-level comments, 25 per page, LIFO by default." |
| 4 | 0:50 | Click **User Name**, **E-mail**, **Date** — each twice; point at the URL | "Sorting by every field, both directions. State lives in the URL." Mention response is instant at 1M rows. |
| 5 | 1:15 | Jump to page 200 via URL | "Deep pages cost the same — served from Elasticsearch." |
| 6 | 1:30 | Expand the hottest thread; scroll; **Показать ещё** | "Unlimited cascading replies. Threads page on the materialised path — this one has ~14k replies." |
| 7 | 2:00 | **New comment**: type `Иван` in User Name, `bad` in e-mail, `<strong>open` in text | Client-side validation messages appear as you type. |
| 8 | 2:25 | Fix fields; select a word, press **[strong]**, then **[a]**; press **Предпросмотр** | "Toolbar wraps the selection. Preview is rendered by the server's sanitiser — no page reload." |
| 9 | 2:55 | Add `<script>alert(1)</script>` to the text, preview again | "Anything outside the four allowed tags is shown as text, never executed." |
| 10 | 3:15 | Attach the 1920×1080 JPG — note "→ будет уменьшено до 320×240" | Client-side file check. |
| 11 | 3:25 | Wrong CAPTCHA → error and a new image; correct one → submit | "CAPTCHA is one-shot: a wrong answer burns it." |
| 12 | 3:45 | The comment appears at the top immediately; image shows "обрабатывается", then the thumbnail | "Write path is one transaction; the image is downscaled by a worker and pushed back over SignalR." |
| 13 | 4:10 | Click the thumbnail — lightbox with animation; Escape closes | "Stored at 320×180 — proportional, not cropped." |
| 14 | 4:30 | Attach the 200 KB `.txt` → rejected; the small `.txt` → posts; open it in the lightbox | "Text files up to 100 KB, shown as text." |
| 15 | 4:55 | Two windows: post in the left, the right shows **"Новых комментариев: 1 — показать"** | "Live updates over WebSocket, offered rather than inserted under the reader's cursor." |
| 16 | 5:20 | Reply inside a thread in one window; it appears in the other thread view | Per-thread SignalR groups. |
| 17 | 5:40 | RabbitMQ UI (`:15672`) → queues; Elasticsearch `_count` | "Outbox → RabbitMQ → indexer → Elasticsearch." |
| 18 | 6:00 | GraphQL IDE (`:5080/graphql`): thread query with nested `replies` | "The Graph requirement: a tree query in one round trip, DataLoader-batched." |
| 19 | 6:20 | Terminal: k6 summary (`loadtests/results`) | "Load test at 200 req/s against the million-row dataset, thresholds enforced." |
| 20 | 6:40 | The deployed site on Azure: post a short comment | "Running in Azure Container Apps; the URL is in the README." |
| 21 | 6:50 | `docs/CHECKLIST.md` scrolled once; `deploy/bicep` tree | "Every requirement mapped to code and a verification step; Azure infrastructure as code." |
| 22 | 7:05 | End | |
