// Streaming an LLM answer, over the GraphQL subscription and over the raw WebSocket.
//
// TRANSPORT=graphql | raw
// LISTENERS=1        one answer, one listener
// LISTENERS=50       one answer, fifty listeners sharing a broadcast id
//
// Time to first token is recorded separately from total time, because for this workload they say
// different things: the first is what a person waits for, the second is how long the model took.

import ws from "k6/ws";
import { check } from "k6";
import { Trend, Counter } from "k6/metrics";

const HOST = `${__ENV.GATEWAY_HOST}:${__ENV.GATEWAY_PORT}`;
const TRANSPORT = __ENV.TRANSPORT || "raw";
const QUESTION = __ENV.QUESTION || "Summarise the bidding activity on lot 38.";
const LISTENERS = Number(__ENV.LISTENERS || 1);
// Every virtual user in a run shares this, so the model generates once however many are listening.
const BROADCAST_ID = `${__ENV.BROADCAST_PREFIX || "bench"}-${__ENV.RUN_ID || "1"}`;

const timeToFirstToken = new Trend("time_to_first_token", true);
const timeToLastToken = new Trend("time_to_last_token", true);
const serverTimeToFirstToken = new Trend("server_time_to_first_token", true);
const tokensReceived = new Counter("tokens_received");
const answersCompleted = new Counter("answers_completed");

export const options = {
  scenarios: {
    default: {
      executor: "shared-iterations",
      vus: LISTENERS,
      iterations: LISTENERS,
      maxDuration: "10m",
    },
  },
  summaryTrendStats: ["avg", "min", "med", "p(95)", "p(99)", "max"],
};

function record(started, firstTokenAt, serverStats) {
  timeToLastToken.add(Date.now() - started);
  if (firstTokenAt) {
    timeToFirstToken.add(firstTokenAt - started);
  }
  if (serverStats && serverStats.ttft) {
    serverTimeToFirstToken.add(serverStats.ttft);
  }
  answersCompleted.add(1);
}

export default function () {
  const started = Date.now();
  let firstTokenAt = null;

  const url = TRANSPORT === "graphql" ? `ws://${HOST}/graphql` : `ws://${HOST}/ws`;

  // k6's ws module has no `subprotocols` option — passing one is silently ignored, the handshake
  // still returns 101, and the server then never starts the subscription. Negotiating through the
  // header is the same thing at the HTTP level, and it actually works.
  const params =
    TRANSPORT === "graphql"
      ? { headers: { "Sec-WebSocket-Protocol": "graphql-transport-ws" } }
      : {};

  const response = ws.connect(url, params, function (socket) {
    socket.on("open", function () {
      if (TRANSPORT === "graphql") {
        socket.send(JSON.stringify({ type: "connection_init" }));
      } else {
        socket.send(JSON.stringify({ question: QUESTION, broadcastId: BROADCAST_ID }));
      }
    });

    socket.on("message", function (raw) {
      const message = JSON.parse(raw);

      if (TRANSPORT === "graphql") {
        if (message.type === "connection_ack") {
          socket.send(
            JSON.stringify({
              id: "1",
              type: "subscribe",
              payload: {
                query: `subscription Ask($q: String!, $b: String) {
                          askAboutData(question: $q, broadcastId: $b) {
                            token done timeToFirstTokenMs totalMs tokenCount } }`,
                variables: { q: QUESTION, b: BROADCAST_ID },
              },
            })
          );
          return;
        }
        if (message.type !== "next") {
          return;
        }
        const chunk = message.payload.data.askAboutData;
        if (chunk.done) {
          record(started, firstTokenAt, { ttft: chunk.timeToFirstTokenMs });
          socket.close();
          return;
        }
        if (chunk.token) {
          if (!firstTokenAt) firstTokenAt = Date.now();
          tokensReceived.add(1);
        }
        return;
      }

      // Raw socket.
      if (message.done) {
        record(started, firstTokenAt, { ttft: message.timeToFirstTokenMs });
        socket.close();
        return;
      }
      if (message.token) {
        if (!firstTokenAt) firstTokenAt = Date.now();
        tokensReceived.add(1);
      }
    });

    socket.setTimeout(function () {
      socket.close();
    }, 480000);
  });

  check(response, { "handshake 101": (r) => r && r.status === 101 });
}

export function handleSummary(data) {
  return { [`/out/${__ENV.OUT_NAME}.json`]: JSON.stringify(data) };
}
