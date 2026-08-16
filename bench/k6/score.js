// GraphQL scoring: one bid at a time, or five hundred in a single call.
//
// SCENARIO=one     N iterations of scoreBid, one bid per request
// SCENARIO=batch   N iterations of scoreBids with all 500 contexts in one request
//
// The same contexts the gRPC and WebSocket runs use, read from the shared payload file, so the
// three transports are doing identical work.

import http from "k6/http";
import { check } from "k6";
import { Trend } from "k6/metrics";

// The model's own time, as reported inside each response. Recording it here is what lets the
// report separate "how long the model took" from "how long the request took" — the difference
// between the two is everything the transport and the services added.
const modelInference = new Trend("model_inference_ms", true);

const INPUTS = JSON.parse(open("/payloads/score_inputs.json"));
const URL = `http://${__ENV.GATEWAY_HOST}:${__ENV.GATEWAY_PORT}/graphql`;
const SCENARIO = __ENV.SCENARIO || "one";

const SCORE_ONE = `query Score($input: BidContextInput!) {
  scoreBid(input: $input) { score level inferenceMs }
}`;

const SCORE_MANY = `query ScoreMany($inputs: [BidContextInput!]!) {
  scoreBids(inputs: $inputs) { totalInferenceMs assessments { score level } }
}`;

export const options = {
  scenarios: {
    default: {
      executor: "shared-iterations",
      vus: Number(__ENV.VUS || 10),
      iterations: Number(__ENV.ITERATIONS || 500),
      maxDuration: "5m",
    },
  },
  // Percentiles the report quotes.
  summaryTrendStats: ["avg", "min", "med", "p(95)", "p(99)", "max"],
};

export default function () {
  const body =
    SCENARIO === "batch"
      ? { query: SCORE_MANY, variables: { inputs: INPUTS } }
      : { query: SCORE_ONE, variables: { input: INPUTS[__ITER % INPUTS.length] } };

  const response = http.post(URL, JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
  });

  let parsed = null;
  try {
    parsed = JSON.parse(response.body);
  } catch {
    parsed = null;
  }

  check(response, {
    "status 200": (r) => r.status === 200,
    "no graphql errors": () => parsed !== null && !parsed.errors,
  });

  const inference =
    SCENARIO === "batch"
      ? parsed?.data?.scoreBids?.totalInferenceMs
      : parsed?.data?.scoreBid?.inferenceMs;
  if (typeof inference === "number") {
    modelInference.add(inference);
  }
}

export function handleSummary(data) {
  return { [`/out/${__ENV.OUT_NAME}.json`]: JSON.stringify(data) };
}
