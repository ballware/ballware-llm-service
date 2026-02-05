FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG GITHUB_USERNAME
ARG GITHUB_PAT

WORKDIR /src

COPY ./Ballware.Llm.Api ./Ballware.Llm.Api
COPY ./Ballware.Llm.Context ./Ballware.Llm.Context
COPY ./Ballware.Llm.Data ./Ballware.Llm.Data
COPY ./Ballware.Llm.Data.Ef ./Ballware.Llm.Data.Ef
COPY ./Ballware.Llm.Data.Ef.Postgres ./Ballware.Llm.Data.Ef.Postgres
COPY ./Ballware.Llm.Embedding ./Ballware.Llm.Embedding
COPY ./Ballware.Llm.Ingestion ./Ballware.Llm.Ingestion
COPY ./Ballware.Llm.Ingestion.Docling ./Ballware.Llm.Ingestion.Docling
COPY ./Ballware.Llm.Jobs ./Ballware.Llm.Jobs
COPY ./Ballware.Llm.Metadata ./Ballware.Llm.Metadata
COPY ./Ballware.Llm.Ollama ./Ballware.Llm.Ollama
COPY ./Ballware.Ml.Service ./Ballware.Ml.Service

WORKDIR "/src/Ballware.Llm.Service"
RUN dotnet nuget add source "https://nuget.pkg.github.com/ballware/index.json" --name github --username "$GITHUB_USERNAME" --password "$GITHUB_PAT" --store-password-in-clear-text
RUN dotnet restore "Ballware.Llm.Service.csproj"
RUN dotnet build "Ballware.Llm.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ballware.Llm.Service.csproj" -c Release -o /app/publish

FROM base AS final
USER app
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Ballware.Llm.Service.dll"]