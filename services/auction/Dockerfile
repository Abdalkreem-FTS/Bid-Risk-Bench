ARG DOTNET_SDK_TAG=10.0
ARG DOTNET_RUNTIME_TAG=10.0
ARG BUF_IMAGE_TAG=1.72.0

FROM bufbuild/buf:${BUF_IMAGE_TAG} AS protogen
WORKDIR /w
COPY buf.yaml buf.gen.yaml ./
COPY proto ./proto
RUN ["buf", "generate"]

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY services/contracts/BidRisk.Contracts.csproj services/contracts/
COPY services/auction/BidRisk.Auction.csproj services/auction/
RUN dotnet restore services/auction/BidRisk.Auction.csproj

COPY services/contracts services/contracts
COPY services/auction services/auction
COPY --from=protogen /w/services/contracts/Generated services/contracts/Generated

RUN dotnet publish services/auction/BidRisk.Auction.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG} AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY db/migrations ./db/migrations

ENV MIGRATIONS_PATH=/app/db/migrations \
    DOTNET_gcServer=0

USER app

EXPOSE 5001
ENTRYPOINT ["dotnet", "BidRisk.Auction.dll"]
