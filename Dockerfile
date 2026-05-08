FROM mcr.microsoft.com/dotnet/sdk:11.0-preview-alpine AS builder
RUN apk add --no-cache clang zlib-dev musl-dev
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY ["Fraude.csproj", "./"]
RUN dotnet restore "Fraude.csproj" -r linux-musl-x64
COPY . .
RUN dotnet publish "Fraude.csproj" -c Release -r linux-musl-x64 -o /app/publish

FROM alpine:3.21
WORKDIR /app
COPY --from=builder /app/publish .
COPY References ./References
ENV RESOURCES_PATH=/resources
ENTRYPOINT ["./Fraude"]