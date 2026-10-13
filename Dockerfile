FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

# ffmpeg converts voice notes to wav for whisper; libgomp1 is needed by the whisper.cpp runtime.
RUN apt-get update \
  && apt-get install -y --no-install-recommends ffmpeg libgomp1 \
  && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/out ./

RUN mkdir /resources

ENTRYPOINT ["dotnet", "atrocity-bot.dll"]
