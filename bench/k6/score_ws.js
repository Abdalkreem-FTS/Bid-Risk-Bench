// The same scoring work over a bare WebSocket.
//
// SCENARIO=one     each iteration opens a socket, sends one context, reads one reply
// SCENARIO=batch   one message carrying all 500 contexts, one reply
// SCENARIO=serial  one socket, 500 messages sent one after another — the "500 one by one" case
//                  as a WebSocket does it, reusing the connection
//
// k6's ws module measures connection time and message round trips; the timings the report uses
// come from the trends recorded here.

import ws from "k6/ws";
import { check } from "k6";
import { Trend, Counter } from "k6/metrics";

const INPUTS = JSON.parse(open("/payloads/score_inputs.json"));
const URL = `ws://${__ENV.GATEWAY_HOST}:${__ENV.GATEWAY_PORT}/ws/score`;
const SCENARIO = __ENV.SCENARIO || "one";
const SERIAL_MESSAGES = Number(__ENV.SERIAL_MESSAGES || 500);

// One entry per scored bid, whatever shape the scenario used, so the three transports can be
// compared on the same unit.
const scoreDuration = new Trend("score_duration", true);
const scoresReceived = new Counter("scores_received");

export const options = {
  scenarios: {
    default: {
      executor: "shared-iterations",
      vus: Number(__ENV.VUS || 10),
      iterations: Number(__ENV.ITERATIONS || 500),
      maxDuration: "5m",
    },
  },
  summaryTrendStats: ["avg", "min", "med", "p(95)", "p(99)", "max"],
};

export default function () {
  const response = ws.connect(URL, {}, function (socket) {
    socket.on("open", function () {
      if (SCENARIO === "batch") {
        socket.send(JSON.stringify({ contexts: INPUTS }));
        return;
      }
      if (SCENARIO === "serial") {
        // Sent one at a time: each reply is awaited before the next goes out, which is what
        // "five hundred bids one by one" means.
        socket.send(JSON.stringify({ context: INPUTS[0] }));
        return;
      }
      socket.send(JSON.stringify({ context: INPUTS[__ITER % INPUTS.length] }));
    });

    let sent = 1;
    let started = Date.now();

    socket.on("message", function (raw) {
      const frame = JSON.parse(raw);
      scoreDuration.add(Date.now() - started);

      if (frame.scores) {
        scoresReceived.add(frame.scores.length);
        socket.close();
        return;
      }

      scoresReceived.add(1);
      check(frame, { "scored": (f) => typeof f.score === "number" });

      if (SCENARIO === "serial" && sent < SERIAL_MESSAGES) {
        started = Date.now();
        socket.send(JSON.stringify({ context: INPUTS[sent % INPUTS.length] }));
        sent += 1;
        return;
      }

      socket.close();
    });

    socket.setTimeout(function () {
      socket.close();
    }, 120000);
  });

  check(response, { "handshake 101": (r) => r && r.status === 101 });
}

export function handleSummary(data) {
  return { [`/out/${__ENV.OUT_NAME}.json`]: JSON.stringify(data) };
}
