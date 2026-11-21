# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /workspace/integrations/intera/zoho-inventory/service
COPY --from=intera-common . /workspace/shared/dotnet/Intera.Common/
COPY Intera.ZohoInventory.Api.csproj ./
RUN dotnet restore -a "$TARGETARCH"
COPY . ./
RUN dotnet publish -c Release -a "$TARGETARCH" --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/App_Data && chown -R "$APP_UID" /app/App_Data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Intera.ZohoInventory.Api.dll"]
