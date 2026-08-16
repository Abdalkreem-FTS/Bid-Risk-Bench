using BidRisk.Auction.Configuration;
using BidRisk.Auction.Data;
using BidRisk.Auction.Domain;
using BidRisk.Auction.Entities;
using BidRisk.Auction.Risk;
using BidRisk.Contracts.Results;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace BidRisk.Auction.Grpc;

public sealed class AuctionGrpcService(
    AuctionDbContext database,
    IRiskClient risk,
    AuctionOptions options,
    TimeProvider clock,
    ILogger<AuctionGrpcService> logger) : AuctionService.AuctionServiceBase
{
    private static readonly Dictionary<string, RejectReason> RejectReasons =
        new(StringComparer.Ordinal)
        {
            [BidErrors.LotClosed.Code] = RejectReason.LotClosed,
            [BidErrors.BelowReserve.Code] = RejectReason.BelowReserve,
            [BidErrors.BelowCurrentPrice.Code] = RejectReason.BelowCurrentPrice,
            [BidErrors.HighRisk.Code] = RejectReason.HighRisk,
            [BidErrors.ScoringUnavailable.Code] = RejectReason.ScoringUnavailable,
        };

    public override async Task<GetLotResponse> GetLot(GetLotRequest request, ServerCallContext context)
    {
        var lot = await FindLotAsync(request.LotId, context.CancellationToken);

        return lot.Match<GetLotResponse>(
            found => new GetLotResponse { Lot = Mapping.ToProto(found, clock.GetUtcNow()) },
            errors => throw Fault(errors[0]));
    }

    public override async Task<ListLotsResponse> ListLots(
        ListLotsRequest request,
        ServerCallContext context)
    {
        var take = request.First > 0 ? request.First : options.DefaultPageSize;
        var lots = await database.Lots.AsNoTracking()
            .OrderBy(l => l.ClosesAt)
            .ThenBy(l => l.Id)
            .Take(take)
            .ToListAsync(context.CancellationToken);

        var now = clock.GetUtcNow();
        var response = new ListLotsResponse();
        response.Lots.AddRange(lots.Select(l => Mapping.ToProto(l, now)));

        return response;
    }

    public override async Task<GetBidHistoryResponse> GetBidHistory(
        GetBidHistoryRequest request,
        ServerCallContext context)
    {
        IQueryable<Entities.Bid> query = database.Bids.AsNoTracking()
            .Where(b => b.LotId == request.LotId)
            .OrderByDescending(b => b.PlacedAt)
            .ThenByDescending(b => b.Id);

        if (request.Limit > 0)
        {
            query = query.Take(request.Limit);
        }

        var bids = await query.ToListAsync(context.CancellationToken);
        var response = new GetBidHistoryResponse();
        response.Bids.AddRange(bids.Select(Mapping.ToProto));

        return response;
    }

    public override async Task<GetBidHistoryBatchResponse> GetBidHistoryBatch(
        GetBidHistoryBatchRequest request,
        ServerCallContext context)
    {
        var response = new GetBidHistoryBatchResponse();
        if (request.LotIds.Count == 0)
        {
            return response;
        }

        var lotIds = request.LotIds.Distinct().ToHashSet();
        var bids = await database.Bids.AsNoTracking()
            .Where(b => lotIds.Contains(b.LotId))
            .OrderByDescending(b => b.PlacedAt)
            .ThenByDescending(b => b.Id)
            .ToListAsync(context.CancellationToken);

        var byLot = bids.ToLookup(b => b.LotId);

        foreach (var lotId in lotIds)
        {
            var lotBids = request.LimitPerLot > 0
                ? byLot[lotId].Take(request.LimitPerLot)
                : byLot[lotId];

            var history = new LotBidHistory { LotId = lotId };
            history.Bids.AddRange(lotBids.Select(Mapping.ToProto));
            response.Histories.Add(history);
        }

        return response;
    }

    public override async Task<PlaceBidResponse> PlaceBid(
        PlaceBidRequest request,
        ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var now = clock.GetUtcNow();

        var parsed = BidRules.ParseAmount(request.Amount);
        if (parsed.IsError)
        {
            throw Fault(parsed.TopError);
        }

        var amount = parsed.Value;

        var lot = await FindLotAsync(request.LotId, cancellationToken);
        if (lot.IsError)
        {
            return await FailAsync(request, amount, now, lot.TopError, null, cancellationToken);
        }

        var bidder = await FindBidderAsync(request.BidderId, cancellationToken);
        if (bidder.IsError)
        {
            return await FailAsync(request, amount, now, bidder.TopError, null, cancellationToken);
        }

        var allowed = BidRules.Validate(Snapshot(lot.Value), amount, now);
        if (allowed.IsError)
        {
            return await FailAsync(request, amount, now, allowed.TopError, null, cancellationToken);
        }

        var scored = await risk.ScoreAsync(
            await BuildContextAsync(request, amount, bidder.Value, lot.Value, now, cancellationToken), cancellationToken);

        if (scored.IsError)
        {
            return await FailAsync(request, amount, now, BidErrors.ScoringUnavailable, null, cancellationToken);
        }

        var assessment = scored.Value;
        if (BidRules.IsTooRisky(assessment.Score, options.RiskRejectThreshold))
        {
            return await FailAsync(request, amount, now, BidErrors.HighRisk, assessment, cancellationToken);
        }

        return await AcceptAsync(request, amount, now, assessment, cancellationToken);
    }

    private async Task<Result<Entities.Lot>> FindLotAsync(long lotId, CancellationToken cancellationToken)
    {
        var lot = await database.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, cancellationToken);

        return lot is null ? BidErrors.LotNotFound(lotId) : lot;
    }

    private async Task<Result<Bidder>> FindBidderAsync(
        long bidderId,
        CancellationToken cancellationToken)
    {
        var bidder = await database.Bidders.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bidderId, cancellationToken);

        return bidder is null ? BidErrors.BidderNotFound(bidderId) : bidder;
    }

    private async Task<BidContext> BuildContextAsync(
        PlaceBidRequest request,
        decimal amount,
        Bidder bidder,
        Entities.Lot lot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousBidAt = await database.Bids.AsNoTracking()
            .Where(b => b.LotId == request.LotId && b.Accepted)
            .MaxAsync(b => (DateTimeOffset?)b.PlacedAt, cancellationToken);

        var bidderBidsOnLot = await database.Bids.AsNoTracking()
            .CountAsync(
                b => b.LotId == request.LotId && b.BidderId == request.BidderId && b.Accepted,
                cancellationToken);

        var bidContext = new BidContext
        {
            Amount = (double)amount,
            CurrentPrice = (double)lot.CurrentPrice,
            BidTime = Timestamp.FromDateTimeOffset(now),
            BidderBidsOnLot = bidderBidsOnLot + 1,
            LotTotalBids = lot.BidCount + 1,
            BidderAccountCreated = Timestamp.FromDateTimeOffset(bidder.CreatedAt),
        };

        if (previousBidAt is { } previous)
        {
            bidContext.PreviousBidTime = Timestamp.FromDateTimeOffset(previous);
        }

        return bidContext;
    }

    private async Task<PlaceBidResponse> AcceptAsync(
        PlaceBidRequest request,
        decimal amount,
        DateTimeOffset now,
        RiskAssessment assessment,
        CancellationToken cancellationToken)
    {
        // Lock the row and check the rules again. The lot can move while we wait for the score.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var locked = await database.Lots
            .FromSql($"SELECT * FROM lots WHERE id = {request.LotId} FOR UPDATE")
            .FirstAsync(cancellationToken);

        var allowed = BidRules.Validate(Snapshot(locked), amount, now);
        if (allowed.IsError)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogInformation(
                "lot {LotId} moved while the bid was being scored — now {Reason}",
                request.LotId,
                allowed.TopError.Code);
            return await FailAsync(
                request, amount, now, allowed.TopError, assessment, cancellationToken);
        }

        var bid = new Entities.Bid
        {
            LotId = request.LotId,
            BidderId = request.BidderId,
            Amount = amount,
            PlacedAt = now,
            Accepted = true,
            RejectReason = null,
            RiskScore = assessment.Score,
            RiskLevel = assessment.Level.ToString(),
            RiskReasons = [.. assessment.Reasons],
            ModelVersion = assessment.ModelVersion,
        };

        database.Bids.Add(bid);
        locked.CurrentPrice = amount;
        locked.BidCount += 1;

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PlaceBidResponse
        {
            Accepted = new BidAccepted { Bid = Mapping.ToProto(bid), Risk = assessment },
        };
    }

    private Task<PlaceBidResponse> FailAsync(
        PlaceBidRequest request,
        decimal amount,
        DateTimeOffset now,
        Error error,
        RiskAssessment? assessment,
        CancellationToken cancellationToken) =>
        // A refused bid is a normal result, so it comes back as Rejected. Only faults throw.
        error.Type == ErrorType.Conflict
            ? RejectAsync(request, amount, now, error, assessment, cancellationToken)
            : throw Fault(error);

    private async Task<PlaceBidResponse> RejectAsync(
        PlaceBidRequest request,
        decimal amount,
        DateTimeOffset now,
        Error error,
        RiskAssessment? assessment,
        CancellationToken cancellationToken)
    {
        var reason = ToRejectReason(error);

        database.Bids.Add(new Entities.Bid
        {
            LotId = request.LotId,
            BidderId = request.BidderId,
            Amount = amount,
            PlacedAt = now,
            // Refused bids are stored too, so "why was this flagged?" can be answered later.
            Accepted = false,
            RejectReason = reason.ToString(),
            RiskScore = assessment?.Score,
            RiskLevel = assessment?.Level.ToString(),
            RiskReasons = assessment is null ? null : [.. assessment.Reasons],
            ModelVersion = assessment?.ModelVersion,
        });
        await database.SaveChangesAsync(cancellationToken);

        var rejected = new BidRejected { Reason = reason, Message = error.Description };
        if (assessment is not null)
        {
            rejected.Risk = assessment;
        }

        return new PlaceBidResponse { Rejected = rejected };
    }

    private static LotSnapshot Snapshot(Entities.Lot lot) =>
        new(lot.ReservePrice, lot.CurrentPrice, lot.Closed, lot.ClosesAt);

    private static RejectReason ToRejectReason(Error error) =>
        RejectReasons.GetValueOrDefault(error.Code, RejectReason.Unspecified);

    private static RpcException Fault(Error error) => new(new Status(
        error.Type switch
        {
            ErrorType.NotFound => StatusCode.NotFound,
            ErrorType.Validation => StatusCode.InvalidArgument,
            ErrorType.Unauthorized => StatusCode.Unauthenticated,
            ErrorType.Forbidden => StatusCode.PermissionDenied,
            _ => StatusCode.Internal,
        },
        error.Description));
}
