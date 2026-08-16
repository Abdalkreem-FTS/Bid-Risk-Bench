-- The auction schema.
--
-- Applied by Evolve at auction-service startup, before the gRPC port opens, so a healthy service
-- always implies a current schema. Evolve checksums this file once it has run: to change the
-- schema, add V2__whatever.sql — never edit a script that has already been applied.

CREATE TABLE bidders (
    id          bigserial PRIMARY KEY,
    username    text        NOT NULL UNIQUE,
    -- The account_age_days feature is derived from this, so it is not decoration.
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE lots (
    id             bigserial      PRIMARY KEY,
    title          text           NOT NULL,
    reserve_price  numeric(12, 2) NOT NULL CHECK (reserve_price >= 0),
    current_price  numeric(12, 2) NOT NULL CHECK (current_price >= 0),
    -- Denormalised: read on every bid to build the model's context, and by the lots listing.
    -- Maintained inside the same transaction that inserts an accepted bid.
    bid_count      integer        NOT NULL DEFAULT 0 CHECK (bid_count >= 0),
    closes_at      timestamptz    NOT NULL,
    closed         boolean        NOT NULL DEFAULT false
);

CREATE TABLE bids (
    id             bigserial      PRIMARY KEY,
    lot_id         bigint         NOT NULL REFERENCES lots (id),
    bidder_id      bigint         NOT NULL REFERENCES bidders (id),
    amount         numeric(12, 2) NOT NULL CHECK (amount > 0),
    placed_at      timestamptz    NOT NULL DEFAULT now(),

    -- Rejected bids are stored too. The LLM has to answer "why was this bid flagged?", the lot
    -- risk aggregate needs them, and an auction that silently forgets refused bids has no audit
    -- trail. The cost is one boolean.
    accepted       boolean        NOT NULL,
    reject_reason  text           NULL,

    -- The score AS IT WAS when the bid was placed: a historical fact, never recomputed. Null only
    -- when scoring was unavailable and the bid was refused for that reason.
    risk_score     double precision NULL CHECK (risk_score IS NULL OR (risk_score >= 0 AND risk_score <= 1)),
    risk_level     text           NULL,
    risk_reasons   text[]         NULL,
    -- Makes scores from different models comparable, and makes "which model refused this?"
    -- answerable months later.
    model_version  text           NULL,

    CONSTRAINT bids_reject_reason_matches_accepted
        CHECK ((accepted AND reject_reason IS NULL) OR (NOT accepted AND reject_reason IS NOT NULL))
);

-- Bid history for one lot, newest first: the LLM prompt and the gap-since-previous-bid feature.
CREATE INDEX bids_lot_id_placed_at_idx ON bids (lot_id, placed_at DESC);

-- "How many bids has this bidder already placed on this lot?" — computed on every incoming bid.
CREATE INDEX bids_bidder_id_lot_id_idx ON bids (bidder_id, lot_id);

-- The lots listing orders by closing time; the risk aggregate reads only scored bids.
CREATE INDEX lots_closes_at_idx ON lots (closes_at);
CREATE INDEX bids_lot_id_risk_score_idx ON bids (lot_id, risk_score) WHERE risk_score IS NOT NULL;
