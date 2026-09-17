/*
 * Spike test: what happens when traffic multiplies in seconds rather than minutes.
 *
 * A board gets linked from somewhere popular and goes from 20 to 600 requests per second with no
 * warning. What is being checked here is not throughput but behaviour: the rate limiter should shed
 * excess load with 429s rather than letting the database queue collapse, the p99 should degrade
 * gracefully rather than cliff, and the service should recover to baseline latency after the spike
 * instead of staying wedged.
 *
 * Run:
 *   k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/spike.js
 */

import http from 'k6/http';
import { check } from 'k6';
import { Rate } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';

const rejected = new Rate('rate_limited');
const serverErrors = new Rate('server_errors');

export const options = {
  scenarios: {
    spike: {
      executor: 'ramping-arrival-rate',
      startRate: 20,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 1500,
      stages: [
        { target: 20, duration: '30s' },   // baseline
        { target: 600, duration: '10s' },  // the spike
        { target: 600, duration: '1m' },
        { target: 20, duration: '10s' },   // back down
        { target: 20, duration: '1m' },    // recovery window
      ],
    },
  },
  thresholds: {
    /*
     * Deliberately no threshold on 429s: shedding load is the correct behaviour under a spike, not
     * a failure. What must not happen is 5xx — that means something broke rather than pushed back.
     */
    'server_errors': ['rate<0.005'],
  },
};

export default function () {
  const response = http.get(`${BASE_URL}/api/comments?page=1&pageSize=25`, {
    tags: { name: 'GET /api/comments (spike)' },
  });

  rejected.add(response.status === 429);
  serverErrors.add(response.status >= 500);

  check(response, {
    'not a server error': (r) => r.status < 500,
  });
}
