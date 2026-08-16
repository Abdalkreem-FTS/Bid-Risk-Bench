// Fetching twenty lots with their risk levels — the N+1 scenario.
//
// The query is identical in both runs; what changes is GATEWAY_BATCHING on the gateway container.
// That is the point: same image, same request, one environment variable.

import http from "k6/http";
import { check } from "k6";

const URL = `http://${__ENV.GATEWAY_HOST}:${__ENV.GATEWAY_PORT}/graphql`;

const QUERY = `{ lots(first: 20) { id title currentPrice bidCount riskLevel } }`;

export const options = {
  scenarios: {
    default: {
      executor: "shared-iterations",
      vus: Number(__ENV.VUS || 10),
      iterations: Number(__ENV.ITERATIONS || 300),
      maxDuration: "5m",
    },
  },
  summaryTrendStats: ["avg", "min", "med", "p(95)", "p(99)", "max"],
};

export default function () {
  const response = http.post(URL, JSON.stringify({ query: QUERY }), {
    headers: { "Content-Type": "application/json" },
  });

  check(response, {
    "status 200": (r) => r.status === 200,
    "twenty lots": (r) => {
      try {
        return JSON.parse(r.body).data.lots.length === 20;
      } catch {
        return false;
      }
    },
  });
}

export function handleSummary(data) {
  return { [`/out/${__ENV.OUT_NAME}.json`]: JSON.stringify(data) };
}
