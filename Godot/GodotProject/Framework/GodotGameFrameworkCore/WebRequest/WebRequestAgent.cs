using Godot;
using System.Text;

namespace GodotGameFramework.Web;

/// <summary>
/// Web 请求代理，封装 Godot HttpRequest 节点。
/// 每个实例代表一个独立的 HTTP 请求。
/// </summary>
[GlobalClass]
public partial class WebRequestAgent : HttpRequest
{
    /// <summary>
    /// 获取当前请求的目标 URL。
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// 发送 GET 请求。
    /// </summary>
    /// <param name="url">请求地址。</param>
    public void Send(string url)
    {
        Url = url;
        Request(url);
    }

    /// <summary>
    /// 发送 POST 请求。字节数据以 UTF-8 编码发送。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="postData">要发送的 POST 数据（UTF-8 编码）。</param>
    public void Send(string url, byte[] postData)
    {
        Url = url;
        string body = Encoding.UTF8.GetString(postData);
        Request(url, null, HttpClient.Method.Post, body);
    }
}
