namespace FluoAxis.Visualization;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.PlotProtocol;

/// <summary>Self-contained research chart. Only the pinned local renderer can become executable content.</summary>
public static class OfflinePlotDocument
{
    public const string PlotlySha256 = "14461f3b4c91c8bb590a99d6d03c3fd031ca40eec07ebab79a5e3eac107cd7ca";
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;

    public static byte[] Create(PlotSpec spec, PlotDataSnapshot data, ReadOnlySpan<byte> pinnedPlotly)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(data);
        if (spec.PlotId != data.PlotId || data.Points.Count > 50_000 || spec.Series.Count > 16)
            throw new ArgumentException("OFFLINE_PLOT_IDENTITY_OR_LIMIT_INVALID");
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(pinnedPlotly)), PlotlySha256, StringComparison.Ordinal))
            throw new ArgumentException("OFFLINE_PLOT_ASSET_HASH_MISMATCH", nameof(pinnedPlotly));
        string specJson = JsonSerializer.Serialize(spec, PlotProtocolJsonContext.Default.PlotSpec);
        string dataJson = JsonSerializer.Serialize(data, PlotProtocolJsonContext.Default.PlotDataSnapshot);
        if (Encoding.UTF8.GetByteCount(specJson) + Encoding.UTF8.GetByteCount(dataJson) > 8 * 1024 * 1024)
            throw new ArgumentException("OFFLINE_PLOT_DATA_LIMIT_EXCEEDED");
        string controllerHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Controller)));
        string prefix = """
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src data:
            """;
        var html = new StringBuilder(prefix);
        html.Append(" 'sha256-").Append(controllerHash).Append("'; style-src 'unsafe-inline'; img-src data: blob:; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'\">")
            .Append("<title>FluoAxis 离线科研图形</title><style>body{margin:0;font:14px 'Segoe UI','Microsoft YaHei',sans-serif;color:#203549}header{padding:16px;background:#edf5f7}#plot{height:75vh;min-height:360px}#point{padding:16px;overflow-wrap:anywhere}#error{padding:20px;color:#a33}</style></head><body>")
            .Append("<header><strong>FluoAxis · 离线科研图形</strong><br>仅供科研使用。此图保存导出时的当前数据页与显示派生列，不代表完整运行或正式判读。三维相机保持固定；点选显示精确来源身份。</header><main id=\"plot\"></main><div id=\"error\" role=\"status\"></div><pre id=\"point\">选择一个数据点查看身份与坐标。</pre>")
            .Append("<script type=\"application/json\" id=\"spec\">").Append(specJson).Append("</script>")
            .Append("<script type=\"application/json\" id=\"data\">").Append(dataJson).Append("</script>")
            .Append("<script src=\"data:application/javascript;base64,").Append(Convert.ToBase64String(pinnedPlotly)).Append("\"></script>")
            .Append("<script>").Append(Controller).Append("</script></body></html>");
        byte[] bytes = Encoding.UTF8.GetBytes(html.ToString());
        if (bytes.Length > MaximumDocumentBytes) throw new ArgumentException("OFFLINE_PLOT_DOCUMENT_LIMIT_EXCEEDED");
        return bytes;
    }

    private const string Controller = """
        (async()=>{"use strict";try{
        const s=JSON.parse(document.getElementById('spec').textContent),d=JSON.parse(document.getElementById('data').textContent);
        const clean=t=>String(t??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        const three=s.kind==='Scatter3D'||s.kind==='Line3D',line=s.kind==='Line2D'||s.kind==='Line3D',both=s.kind==='ScatterLine2D';
        const axis=a=>({title:{text:clean(a.label)},type:a.scale==='Log'?'log':'linear',range:a.minimum==null||a.maximum==null?undefined:a.scale==='Log'?[Math.log10(a.minimum),Math.log10(a.maximum)]:[a.minimum,a.maximum]});
        const traces=s.series.map(series=>{const p=d.points.filter(p=>p.seriesId===series.seriesId);return {type:three?'scatter3d':'scatter',mode:both?'lines+markers':line?'lines':'markers',name:clean(series.label),visible:series.visible?true:'legendonly',x:p.map(v=>v.x),y:p.map(v=>v.y),...(three?{z:p.map(v=>v.z)}:{}),customdata:p.map(v=>v.pointId),text:p.map(v=>clean(v.label)),error_y:p.some(v=>v.errorY!=null)?{type:'data',array:p.map(v=>v.errorY??0),visible:true}:undefined,marker:{size:series.markerSize},hovertemplate:'%{text}<br>x=%{x}<br>y=%{y}'+(three?'<br>z=%{z}':'')+'<extra></extra>'};});
        const layout={title:{text:clean(s.title)},showlegend:s.showLegend,margin:{t:60,l:70,r:30,b:65}};
        if(three){layout.scene={xaxis:axis(s.x),yaxis:axis(s.y),zaxis:axis(s.z),dragmode:false,aspectmode:'cube',camera:{eye:{x:s.camera.eyeX,y:s.camera.eyeY,z:s.camera.eyeZ},projection:{type:s.camera.projection==='Orthographic'?'orthographic':'perspective'}}};}else{layout.xaxis=axis(s.x);layout.yaxis=axis(s.y);}
        const config={responsive:true,displaylogo:false,scrollZoom:false,doubleClick:three?false:'reset',modeBarButtonsToRemove:three?['zoom3d','pan3d','orbitRotation','tableRotation','resetCameraDefault3d','resetCameraLastSave3d']:[]};
        const element=document.getElementById('plot');await Plotly.newPlot(element,traces,layout,config);
        element.on('plotly_click',event=>{const id=event?.points?.[0]?.customdata,p=d.points.find(v=>v.pointId===id);if(p)document.getElementById('point').textContent=JSON.stringify(p,null,2);});
        }catch{document.getElementById('error').textContent='无法绘图：请确认浏览器支持本地图形与所需 WebGL。原始快照仍保存在文档中。';}})();
        """;
}
