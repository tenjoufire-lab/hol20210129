#r "Newtonsoft.Json"

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Configuration;
using Newtonsoft.Json;
using System.Globalization;

public static void Run(Stream myBlob, string name, out object outputDocument, ILogger log)
{
    log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

    //環境変数（アプリケーション設定）からCustomVisionの予測APIの接続情報を取得
    string predectionKey = Environment.GetEnvironmentVariable("PREDICTION_KEY");
    string endpoint = Environment.GetEnvironmentVariable("ENDPOINT");

    //環境変数（アプリケーション設定）から予測APIで受け取った人物判定の閾値を取得
    double th = Double.Parse(Environment.GetEnvironmentVariable("THRESHOLD"));

    //API送信用HttpClient
    var client = new HttpClient();
    // Request headersの設定
    client.DefaultRequestHeaders.Add("Prediction-Key", predectionKey);
    // Prediction URL - replace this example URL with your valid Prediction URL.
    string url = endpoint;

    //必要な変数を宣言
    int peopleCount = 0;
    HttpResponseMessage response;
    byte[] byteData;

    //BlobからStreamとして受け取った画像データをバイナリに変換
    using (MemoryStream ms = new MemoryStream())
    {
        byte[] buf = new byte[32768]; // 一時バッファ
        while (true)
        {
            // ストリームから一時バッファに読み込む
            int read = myBlob.Read(buf, 0, buf.Length);
            if (read > 0)
            {
                // 一時バッファの内容をメモリ・ストリームに書き込む
                ms.Write(buf, 0, read);
            }
            else
            {
                break;
            }
        }
        // メモリ・ストリームの内容をバイト配列に格納
        byteData = ms.ToArray();
    }

    //予測APIのエンドポイントへ画像を送信
    using (var content = new ByteArrayContent(byteData))
    {
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response = client.PostAsync(url, content).Result;
        var jsonString = response.Content.ReadAsStringAsync().Result;
        peopleCount = CountPeople(jsonString, th);
    }
    log.LogInformation($"{peopleCount}人検出されました");

    //Cosmos DBのドキュメントを作成
    var culture = CultureInfo.CreateSpecificCulture("ja-JP");
    outputDocument = new { Id = Guid.NewGuid(), Timestring = DateTime.UtcNow.AddHours(9.0).ToString("u", culture), PeopleCount = peopleCount, Place = "TEST2" };
}

private static int CountPeople(string jsonString, double th)
{
    var peopleCount = 0;
    var jsonobject = JsonConvert.DeserializeObject<PredictionResult>(jsonString);

    foreach (var detectedObject in jsonobject.predictions)
    {
        //確信度がある閾値よりも高い場合、人と認定
        if (detectedObject.probability > th)
        {
            peopleCount++;
        }
    }
    return peopleCount;
}

public class PredictionResult
{
    public string id { get; set; }
    public string project { get; set; }
    public string iteration { get; set; }
    public string created { get; set; }
    public PredictionObject[] predictions { get; set; }
}

public class PredictionObject
{
    public string tagId { get; set; }
    public string tagName { get; set; }
    public double probability { get; set; }
}
