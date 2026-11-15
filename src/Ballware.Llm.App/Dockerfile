FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

ARG GITHUB_USERNAME
ARG GITHUB_PAT
ARG DEVEXPRESS_NUGETFEED
RUN [ -z "$DEVEXPRESS_NUGETFEED" ] && echo "DEVEXPRESS_NUGETFEED needs to be defined" && exit 1 || true
ARG DEVEXPRESS_LICENSE
RUN [ -z "$DEVEXPRESS_LICENSE" ] && echo "DEVEXPRESS_LICENSE needs to be defined" && exit 1 || true

WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG GITHUB_USERNAME
ARG GITHUB_PAT
ARG DEVEXPRESS_NUGETFEED
ARG DEVEXPRESS_LICENSE

WORKDIR /src

COPY ./Ballware.Llm.Api ./Ballware.Llm.Api
COPY ./Ballware.Llm.App ./Ballware.Llm.App
COPY ./Ballware.Llm.Context ./Ballware.Llm.Context
COPY ./Ballware.Llm.Data ./Ballware.Llm.Data
COPY ./Ballware.Llm.Data.Ef ./Ballware.Llm.Data.Ef
COPY ./Ballware.Llm.Data.Ef.Postgres ./Ballware.Llm.Data.Ef.Postgres
COPY ./Ballware.Llm.Embedding ./Ballware.Llm.Embedding
COPY ./Ballware.Llm.Embedding.OpenAi ./Ballware.Llm.Embedding.OpenAi
COPY ./Ballware.Llm.Embedding.SemanticKernel ./Ballware.Llm.Embedding.SemanticKernel
COPY ./Ballware.Llm.Hosting ./Ballware.Llm.Hosting
COPY ./Ballware.Llm.Inference ./Ballware.Llm.Inference
COPY ./Ballware.Llm.Inference.OpenAi ./Ballware.Llm.Inference.OpenAi
COPY ./Ballware.Llm.Metadata ./Ballware.Llm.Metadata
COPY ./Ballware.Llm.Tools ./Ballware.Llm.Tools
COPY ./Ballware.Llm.Tools.Common ./Ballware.Llm.Tools.Common
COPY ./Ballware.Llm.Tools.Embedding ./Ballware.Llm.Tools.Embedding

WORKDIR "/src/Ballware.Llm.App"
RUN dotnet nuget add source "https://nuget.pkg.github.com/ballware/index.json" --name github --username "$GITHUB_USERNAME" --password "$GITHUB_PAT" --store-password-in-clear-text
RUN dotnet nuget add source "$DEVEXPRESS_NUGETFEED" --name devexpress
RUN mkdir -p "$HOME/.config/DevExpress" && printf '%s' "$DEVEXPRESS_LICENSE" > "$HOME/.config/DevExpress/DevExpress_License.txt"
RUN dotnet restore "Ballware.Llm.App.csproj"
RUN dotnet build "Ballware.Llm.App.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ballware.Llm.App.csproj" -c Release -o /app/publish

FROM base AS final
USER app
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Ballware.Llm.App.dll"]
