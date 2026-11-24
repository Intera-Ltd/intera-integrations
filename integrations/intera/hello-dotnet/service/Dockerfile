# syntax=docker/dockerfile:1

# Build stage: runs on the build machine's architecture and cross-publishes
# for the target one, so multi-platform builds do not need emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
ARG TARGETARCH
WORKDIR /src
COPY HelloDotnet.csproj ./
RUN dotnet restore -a "$TARGETARCH"
COPY . ./
RUN dotnet publish -c Release -a "$TARGETARCH" --no-restore -o /app

# Runtime stage: ASP.NET runtime only, no SDK, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:dccacae9e0e103b5d3ba5fbe442dd8d3316b990cd8f2ec9be0a0ec9db0c0e520 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
# The base image ships the unprivileged "app" user (UID 1654).
USER $APP_UID
ENTRYPOINT ["dotnet", "HelloDotnet.dll"]
