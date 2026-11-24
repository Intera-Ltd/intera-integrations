# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
ARG TARGETARCH
WORKDIR /workspace/integrations/intera/teamcity/service
COPY --from=intera-common . /workspace/shared/dotnet/Intera.Common/
COPY Intera.TeamCity.Api.csproj ./
RUN dotnet restore -a "$TARGETARCH"
COPY . ./
RUN dotnet publish -c Release -a "$TARGETARCH" --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:ca396ece3f2a2e62c7a03f1d7d710b8bc7e8c9dea5597d091178d9f5bfca13f0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Intera.TeamCity.Api.dll"]
