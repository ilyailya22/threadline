/*
 * Write scenario: posting comments through the full pipeline.
 *
 * Every submission does what a real one does — fetch a CAPTCHA, answer it, post multipart form
 * data. There is no way to answer a CAPTCHA from a script, so the API exposes a load-test bypass
 * that is enabled only when LoadTest:Enabled is true and is refused outright in production (see
 * docs/LOAD-TESTING.md). Bypassing it is the honest choice: the alternative is a "write benchmark"
 * that measures nothing but the 400 the CAPTCHA returns.
 *
 * Run:
 *   k6 run -e BASE_URL=http://localhost:5080 -e BYPASS=<token> loadtests/k6/write.js
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomIntBetween, randomString } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';
import { FormData } from 'https://jslib.k6.io/formdata/0.0.2/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const BYPASS = __ENV.BYPASS || '';

const writeLatency = new Trend('write_latency', true);
const errorRate = new Rate('errors');

export const options = {
  scenarios: {
    write: {
      executor: 'ramping-arrival-rate',
      startRate: 2,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 200,
      stages: [
        { target: 10, duration: '1m' },
        { target: 30, duration: '3m' },  // ~2.6M comments/day if sustained — well past the target
        { target: 0, duration: '30s' },
      ],
    },
  },
  thresholds: {
    // The write path is one insert plus one outbox row, so it should stay fast even under load.
    // If this threshold starts failing, something has crept back onto the request thread.
    'write_latency': ['p(95)<400', 'p(99)<1000'],
    'errors': ['rate<0.02'],
  },
};

export default function () {
  const captcha = http.get(`${BASE_URL}/api/captcha`, { tags: { name: 'GET /api/captcha' } });

  if (!check(captcha, { 'captcha: 200': (r) => r.status === 200 })) {
    errorRate.add(true);
    return;
  }

  const captchaId = captcha.headers['X-Captcha-Id'];

  // multipart/form-data, exactly as the browser sends it. A plain object would be sent URL-encoded,
  // which the endpoint rejects with 415 — a "write benchmark" that would measure nothing but that.
  const form = new FormData();
  const user = randomIntBetween(1, 100000);

  form.append('userName', `loadtest${user}`);
  form.append('email', `loadtest${user}@example.com`);
  form.append('text', `Load test message ${randomString(24)} with <strong>markup</strong>.`);
  form.append('captchaId', captchaId);
  form.append('captchaAnswer', BYPASS);

  const response = http.post(`${BASE_URL}/api/comments`, form.body(), {
    headers: { 'Content-Type': `multipart/form-data; boundary=${form.boundary}` },
    tags: { name: 'POST /api/comments' },
  });

  writeLatency.add(response.timings.duration);

  errorRate.add(
    !check(response, {
      'write: 201': (r) => r.status === 201,
      'write: returns id': (r) => !!r.json('id'),
    }),
  );

  sleep(randomIntBetween(1, 3));
}
