# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
ARG TARGETARCH
WORKDIR /workspace/integrations/intera/business-central/service
COPY --from=intera-common . /workspace/shared/dotnet/Intera.Common/
COPY Intera.BusinessCentral.Api.csproj ./
RUN dotnet restore -a "$TARGETARCH"
COPY . ./
RUN dotnet publish -c Release -a "$TARGETARCH" --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Intera.BusinessCentral.Api.dll"]
