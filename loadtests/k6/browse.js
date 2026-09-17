/*
 * Read-heavy browsing scenario — the shape of the traffic the assignment describes.
 *
 * 100,000 users in 24 hours averages a little over one visitor per second, but averages are not
 * what breaks a system. A visitor loads the list, sorts it, pages through it and opens a thread or
 * two, so a single session is roughly 6–10 requests, and real traffic is not flat: the evening peak
 * on a board like this is comfortably 10× the daily mean. This test therefore targets a sustained
 * 200 requests/second of reads, which is an order of magnitude above the stated average — the point
 * is to find the ceiling, not to confirm the average is survivable.
 *
 * Run:
 *   k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/browse.js
 */

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const PAGE_SIZE = 25;

const listLatency = new Trend('list_latency', true);
const threadLatency = new Trend('thread_latency', true);
const errorRate = new Rate('errors');

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-arrival-rate',
      startRate: 10,
      timeUnit: '1s',
      preAllocatedVUs: 100,
      maxVUs: 600,
      stages: [
        { target: 50, duration: '1m' },   // warm the caches
        { target: 200, duration: '2m' },  // ramp to the target
        { target: 200, duration: '5m' },  // hold: this is the measurement window
        { target: 0, duration: '1m' },    // ramp down
      ],
    },
  },
  thresholds: {
    /*
     * Service level objectives. These are assertions, not decoration: k6 exits non-zero when one
     * is breached, so a regression fails CI rather than being noticed later in a graph.
     */
    'http_req_failed': ['rate<0.01'],
    'list_latency': ['p(95)<300', 'p(99)<800'],
    'thread_latency': ['p(95)<400'],
    'errors': ['rate<0.01'],
  },
};

const SORTS = [
  { sortBy: 'createdAt', direction: 'descending' }, // the default, and the most requested
  { sortBy: 'createdAt', direction: 'ascending' },
  { sortBy: 'userName', direction: 'ascending' },
  { sortBy: 'userName', direction: 'descending' },
  { sortBy: 'email', direction: 'ascending' },
];

function pickSort() {
  // Weighted towards the default: on a real board most visitors never touch the sort controls, and
  // a uniform distribution would understate how much work the cache actually absorbs.
  return Math.random() < 0.7 ? SORTS[0] : SORTS[randomIntBetween(1, SORTS.length - 1)];
}

export default function () {
  let firstPage;

  group('list', () => {
    const sort = pickSort();

    // Deep pages are rare in real traffic, so most requests hit the first few.
    const page = Math.random() < 0.8 ? randomIntBetween(1, 3) : randomIntBetween(4, 40);

    const response = http.get(
      `${BASE_URL}/api/comments?page=${page}&pageSize=${PAGE_SIZE}&sortBy=${sort.sortBy}&direction=${sort.direction}`,
      { tags: { name: 'GET /api/comments' } },
    );

    listLatency.add(response.timings.duration);
    const ok = check(response, {
      'list: 200': (r) => r.status === 200,
      'list: has items array': (r) => Array.isArray(r.json('items')),
      'list: page size honoured': (r) => (r.json('items') || []).length <= PAGE_SIZE,
    });

    errorRate.add(!ok);

    if (ok && page <= 3) {
      firstPage = response.json('items');
    }
  });

  // Reading a thread is the second most common thing a visitor does.
  if (firstPage && firstPage.length > 0 && Math.random() < 0.6) {
    group('thread', () => {
      const comment = firstPage[randomIntBetween(0, firstPage.length - 1)];

      const response = http.get(`${BASE_URL}/api/comments/${comment.id}/thread?maxDepth=10`, {
        tags: { name: 'GET /api/comments/{id}/thread' },
      });

      threadLatency.add(response.timings.duration);
      errorRate.add(!check(response, { 'thread: 200': (r) => r.status === 200 }));
    });
  }

  // Think time. Without it the test measures how fast a machine can loop, not how a site behaves.
  sleep(randomIntBetween(1, 4));
}
