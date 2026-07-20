'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "Official"
    '注册表文件夹
    Public Const RegFolder As String = "PCL"
    '用于微软登录的 ClientId
    Public Const OAuthClientId As String = "fe72edc2-3a6f-4280-90e8-e2beb64ce7e1"
    'CurseForge API Key
    Public ReadOnly CurseForgeAPIKey As String = Encoding.Default.GetString(Convert.FromBase64String("JDJhJDEwJDdXV1YxV0VMY3dYOFhwN2Q2OE1Icy41Z3JUeVpWYTZTeW55ZVN5TWgxcnNFODluSWEwOXpP"))
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = "https://ap-shanghai.cls.tencentcs.com/track?topic_id=pcl-remote-1253424809"

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If ApplicationStartTick < 1 Then Return UrlWithMark
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Dim Url = UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
        Dim Nonce = RandomInteger(0, 2147483645).ToString("x").EnsureLength("0"c, 8)
        Dim Timestamp = GetUnixTimestampUtc().ToString
        Dim PathWithQuery = Url.Substring(Url.IndexOfF("://") + 3)
        PathWithQuery = PathWithQuery.Substring(PathWithQuery.IndexOfF("/"))
        Dim Sign = CryptographyUtils.ComputeHash(String.Join("-", {PathWithQuery, Timestamp, Nonce, "0", "PCL2Server"}), CryptographyUtils.HashMethod.Sha1)
        Return Url & If(Url.Contains("?"), "&", "?") & "sign=" & String.Join("-", {Timestamp, Nonce, "0", Sign})
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If ApplicationStartTick < 1 Then Return
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Referrer = New Uri($"http://{VersionCode}.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0
    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If Not ThemeCheckOne(If(NewTheme >= 0, NewTheme, If(ThemeNow >= 0, ThemeNow, 0))) Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme

            ColorHue = 210
            ColorSat = 85
            ColorLightAdjust = 0
            Select Case ThemeDontClick
                Case 1
                    ColorLightAdjust = 999
                Case 2
                    ColorHue = RandomInteger(0, 359)
                    ColorSat = RandomInteger(40, 70)
                    ColorLightAdjust = RandomInteger(-20, 20)
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
            If ThemeNow <> 12 AndAlso ThemeNow <> 14 AndAlso ThemeDontClick <> 2 Then Logger.Info($"刷新主题：{ThemeNow}")
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            If ThemeNow = 5 Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 15)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 25)})
            ElseIf ThemeNow <> 12 AndAlso ThemeDontClick <> 2 Then
                Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue - 21, ColorSat, 53 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.33, .Color = New MyColor().FromHSL2(ColorHue - 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.67, .Color = New MyColor().FromHSL2(ColorHue + 7, ColorSat, 47 + ColorLightAdjust)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + 21, ColorSat, 53 + ColorLightAdjust)})
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            FrmMain.ImgTitle.Source = If(ThemeNow >= 5 AndAlso ThemeNow <> 14, $"{PathImage}Themes/{ThemeNow}.png", Nothing)
            FrmMain.ImgTitle.Opacity = If(ThemeNow = 13, 0.25, 0.5)
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        RunInUi(
        Sub()
            Try
                Dim CurrentTheme = Settings.Get(Of Integer)("UiLauncherTheme")
                If Not ThemeCheckOne(CurrentTheme) Then
                    Logger.Warn($"检测到尚未解锁的主题：{CurrentTheme}，已重置为默认主题", LogBehavior.ToastIfDebug)
                    Settings.Set("UiLauncherTheme", 0)
                End If
                If EffectSetup AndAlso FrmSetupUI IsNot Nothing Then
                    Dim AvailableThemes As New List(Of Integer)
                    For Id = 0 To 14
                        If ThemeCheckOne(Id) Then AvailableThemes.Add(Id)
                    Next
                    AvailableThemes.Add(42)
                End If
            Catch ex As Exception
                If TypeOf If(ex.InnerException, ex) Is FormatException Then
                    Logger.Error(ex, "解锁的主题列表存档已损坏，主题解锁已被重置", LogBehavior.Alert)
                    Settings.Set("UiLauncherThemeHide2", "")
                Else
                    Logger.Error(ex, "检查主题失败", LogBehavior.AlertThenFeedback)
                End If
            End Try
        End Sub)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        If New Integer() {0, 1, 2, 3, 4, 42}.Contains(Id) Then Return True
        If PotatoFeatures.Contains("Theme" & Id) Then Return True
        Select Case Id
            Case 8
                Return CurrentRank >= DonationRank.Rank23
            Case 14
                Return Enumerable.Range(5, 9).Count(AddressOf ThemeCheckOne) >= 5
            Case Else
                If Settings.Get(Of String)("UiLauncherThemeHide").Contains("7") Then ThemeUnlock(7, ShowDoubleHint:=False)
                Return Settings.Get(Of String)("UiLauncherThemeHide2").Split("|"c).Contains(Id.ToString)
        End Select
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Private IsUpdating As Boolean = False
    Private IsSilentUpdate As Boolean = False
    Private IsUiCheckingUpdate As Boolean = False
    Private UpdateMark As String = "EmptyMark********************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************************"

    Friend Sub UpdateCheckByButton()
        If IsUiCheckingUpdate Then
            Hint("正在检查更新，请稍候！")
            Return
        End If
        IsUiCheckingUpdate = True
        RunInThread(
        Sub()
            Try
                Hint("正在检查更新，请稍候！")
                ServerLoader.WaitForExit(IsForceRestart:=True)
                Dim IsNewest = PageSetupSystem.IsLauncherNewest()
                If Not IsNewest.HasValue Then
                    Hint("连接 PCL 服务器失败，请确认系统时间是否准确，尝试将 PCL 加入杀毒软件或防火墙白名单，然后重启 PCL！", HintType.Red)
                ElseIf IsNewest.Value Then
                    Hint($"已经是最新{BuildTypeDisplay} {VersionBaseName}，不需要更新啦，可以直接使用！", HintType.Green)
                Else
                    If Not HasUpdatePermission Then
                        InputPotatoCode(IsUpdating:=True)
                        If Not HasUpdatePermission Then Return
                    ElseIf MyMsgBox("发现启动器更新，是否立即下载？", "发现更新！", "更新！冲！", "取消") = 2 Then
                        Return
                    End If
                    UpdateStart(Silent:=False)
                End If
            Catch ex As Exception
                Logger.Error(ex, "尝试手动开始更新失败", LogBehavior.AlertThenFeedback)
            Finally
                IsUiCheckingUpdate = False
            End Try
        End Sub)
    End Sub

    Private Sub UpdateStart(Silent As Boolean, Optional UrlsOverride As IEnumerable(Of String) = Nothing)
        If IsUpdating Then
            If Not Silent Then
                Hint("PCL 正在下载更新，更新结束时将自动重启，请稍候！")
                IsSilentUpdate = False
            End If
            If IsUpdateWaitingRestart Then UpdateRestart(TriggerRestartAndByEnd:=True)
            Return
        End If
        IsUpdating = True
        IsSilentUpdate = Silent
        If Not IsSilentUpdate Then Hint("PCL 正在下载更新，更新结束时将自动重启，请稍候！")
        Try
            Dim Urls As List(Of String) = Nothing
            Dim Extension As String = Nothing
            Dim LocalPath As String = Nothing
            Dim Loader As LoaderCombo(Of String) = Nothing
            Loader = New LoaderCombo(Of String)("启动器更新", New LoaderBase() {
                New LoaderTask(Of String, List(Of NetFile))("获取更新信息",
                Sub(Task)
                    If ServerLoader.State = LoadState.Loading Then ServerLoader.WaitForExit()
                    If UrlsOverride IsNot Nothing Then
                        Urls = UrlsOverride.ToList()
                    ElseIf ServerConfig IsNot Nothing AndAlso ServerConfig("Update") IsNot Nothing AndAlso ServerConfig("Update")(BuildTypes.Release.ToString) IsNot Nothing Then
                        Dim UpdateInfo = CType(ServerConfig("Update")(BuildTypes.Release.ToString), JObject)
                        If UpdateInfo.ContainsKey("PatchUrls") AndAlso UpdateMark.StartsWithF("Empty") Then
                            Urls = UpdateInfo("PatchUrls").ToObject(Of List(Of String))()
                        ElseIf UpdateInfo.ContainsKey("DownloadUrls") Then
                            Urls = UpdateInfo("DownloadUrls").ToObject(Of List(Of String))()
                        End If
                    End If
                    If Urls Is Nothing OrElse Not Urls.Any Then
                        IsUpdating = False
                        Throw New Exception("更新失败：服务器没有提供有效的更新包下载链接，或无法连接到服务器！")
                    End If
                    Urls = Urls.Select(Function(u) ArgumentReplace(u, AddressOf WebUtility.HtmlEncode)).ToList()
                    Extension = "." & Urls.First.BeforeLast("{CDN}").AfterLast(".")
                    LocalPath = $"{RequestTaskTempFolder()}{VersionBaseName}.{CInt(BuildType)}{Extension}"
                    Logger.Info($"更新开始，静默：{IsSilentUpdate}，扩展名：{Extension}，下载目标：{LocalPath}，URL：{Urls.Join("，")}")
                    Task.Output = New List(Of NetFile) From {New NetFile(Urls, LocalPath)}
                End Sub) With {.ProgressWeight = 0.1},
                New LoaderDownload("下载更新文件", New List(Of NetFile)) With {.ProgressWeight = 1},
                New LoaderTask(Of String, Integer)("安装更新",
                Sub(Task)
                    Dim TargetPath = Paths.Base & "PCL\Plain Craft Launcher 2.exe"
                    If FileUtils.Exists(TargetPath) Then
                        FileUtils.Delete(TargetPath)
                        Logger.Info($"已清理已存在的更新文件：{TargetPath}")
                    End If
                    If Extension = ".zip" Then
                        Logger.Info($"解压更新文件：{LocalPath}")
                        FileUtils.ExtractToDirectory(LocalPath, Paths.Base & "PCL\")
                    Else
                        Logger.Info($"应用补丁文件：{PathExe} + {LocalPath} → {TargetPath}")
                        MsDelta.Apply(PathExe, LocalPath, TargetPath)
                    End If
                    If IsSilentUpdate Then
                        IsUpdateWaitingRestart = True
                        Return
                    End If
                    If McLaunchLoader.State = LoadState.Loading Then
                        Hint("更新已准备就绪，PCL 将在游戏启动完成后重启！", HintType.Green)
                        Do While McLaunchLoader.State = LoadState.Loading
                            Thread.Sleep(10)
                        Loop
                    End If
                    UpdateRestart(TriggerRestartAndByEnd:=True)
                End Sub) With {.ProgressWeight = 0.1}
            })
            Loader.OnStateChanged =
            Sub(_Loader)
                If Loader.State = LoadState.Failed Then
                    If IsSilentUpdate Then
                        Logger.Warn(Loader.Error, "启动器静默更新失败", LogBehavior.ToastIfDebug)
                    ElseIf Loader.Error.GetDisplay(False).Contains("(403)") Then
                        MyMsgBox("你的系统时间可能并不准确，导致下载验证未通过。" & vbCrLf & "请在校对、修改系统时间后再次尝试更新。", "启动器更新失败")
                    ElseIf Loader.Error.GetDisplay(False).Contains("(404)") Then
                        MyMsgBox("未找到更新包。可能的原因有：" & vbCrLf & "- 你所使用的 PCL 不是官方版本" & vbCrLf & "- 服务器异常", "启动器更新失败")
                    ElseIf TypeOf Loader.Error Is FileNotFoundException Then
                        If MyMsgBox("由于被 Windows 安全中心拦截，或者存在权限问题，导致 PCL 无法更新。" & vbCrLf & "请将 PCL 所在文件夹加入白名单！", "更新失败", "查看帮助", "确定", "", IsWarn:=True) = 1 Then
                            CustomEvent.Raise(CustomEvent.EventType.打开帮助, "启动器/Microsoft Defender 添加排除项.json")
                        End If
                    Else
                        Logger.Error(Loader.Error, "启动器更新失败", LogBehavior.AlertThenFeedback)
                    End If
                End If
                If Loader.State = LoadState.Finished OrElse Loader.State = LoadState.Failed OrElse Loader.State = LoadState.Canceled Then IsUpdating = False
            End Sub
            Loader.Start()
            RunInUi(
            Sub()
                If Not IsSilentUpdate Then
                    LoaderTaskbarAdd(Loader)
                    FrmMain.BtnExtraDownload.ShowRefresh()
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "开始启动器更新失败", LogBehavior.AlertThenFeedback)
            IsUpdating = False
        End Try
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        Try
            IsUpdateWaitingRestart = False
            Dim UpdateFile = Paths.Base & "PCL\Plain Craft Launcher 2.exe"
            Dim Arguments = $"--update {Process.GetCurrentProcess().Id} ""{AppDomain.CurrentDomain.SetupInformation.ApplicationName}"" ""{AppDomain.CurrentDomain.SetupInformation.ApplicationName}"" {TriggerRestartAndByEnd}"
            Logger.Info($"更新程序启动，参数：{Arguments}")
            StartProcess(New ProcessStartInfo(UpdateFile) With {
                .WindowStyle = ProcessWindowStyle.Hidden,
                .CreateNoWindow = True,
                .Arguments = Arguments
            })
            If TriggerRestartAndByEnd Then
                FrmMain.EndProgram(SendWarning:=False)
                Logger.Info("已由于更新强制结束程序")
            End If
        Catch ex As System.ComponentModel.Win32Exception
            Logger.Warn(ex, "自动更新时触发 Win32 错误，疑似被拦截", LogBehavior.ToastIfDebug)
            If MyMsgBox("由于被 Windows 安全中心拦截，或者存在权限问题，导致 PCL 无法更新。" & vbCrLf &
                        $"请将 PCL 所在文件夹加入白名单，或者手动用 {Paths.Base}PCL\Plain Craft Launcher 2.exe 替换当前文件！",
                        "更新失败", "查看帮助", "确定", "", IsWarn:=True) = 1 Then
                CustomEvent.Raise(CustomEvent.EventType.打开帮助, "启动器/Microsoft Defender 添加排除项.json")
            End If
        End Try
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            Process.GetProcessById(ProcessId).Kill()
        Catch
        End Try
        Dim OldFilePath = Paths.Base.Substring(0, Paths.Base.Length - 4) & PathUtils.GetLastPart(OldFileName)
        Dim NewFilePath = Paths.Base.Substring(0, Paths.Base.Length - 4) & PathUtils.GetLastPart(NewFileName)
        Dim FormatFailure As Func(Of String, String) =
        Function(Reason)
            Return $"由于{Reason}，PCL 无法完成更新。{vbCrLf}请依次尝试：{vbCrLf}" &
                   If(Paths.Base.StartsWithF(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), True) OrElse
                      Paths.Base.StartsWithF(Environment.GetFolderPath(Environment.SpecialFolder.Personal), True),
                      " - 将 PCL 文件移动到桌面、文档、C 盘以外的文件夹（这或许可以一劳永逸地解决权限问题）" & vbCrLf, "") &
                   If(Paths.Base.StartsWithF("C", True),
                      " - 将 PCL 文件移动到 C 盘以外的文件夹（这或许可以一劳永逸地解决权限问题）" & vbCrLf, "") &
                   If(Not WindowsUtils.HasAdminRole(), " - 右键 PCL，选择以管理员身份运行" & vbCrLf, "") &
                   $" - 手动剪切 PCL 文件夹中的新版本程序，覆盖原程序：{vbCrLf}   将位于 {PathExe}{vbCrLf}   的程序{vbCrLf}   剪切到 {NewFilePath} 并覆盖即可。"
        End Function

        Dim DeleteException As Exception = Nothing
        For Retry = 0 To 4
            Try
                FileUtils.Delete(OldFilePath)
                FileUtils.Delete(NewFilePath)
                If Not FileUtils.Exists(OldFilePath) AndAlso Not FileUtils.Exists(NewFilePath) Then Exit For
            Catch ex As Exception
                DeleteException = ex
            End Try
            Thread.Sleep(Retry * 500 + 500)
        Next

        If FileUtils.Exists(OldFilePath) OrElse FileUtils.Exists(NewFilePath) Then
            If TypeOf DeleteException Is UnauthorizedAccessException Then
                MsgBox(FormatFailure("删除老版本文件时权限不足") & vbCrLf & vbCrLf & "详细错误信息：" & vbCrLf & DeleteException.GetDisplay(True), MsgBoxStyle.Critical, "更新失败")
            Else
                MsgBox(FormatFailure("删除老版本文件时出错") & vbCrLf & vbCrLf & "详细错误信息：" & vbCrLf & If(DeleteException, New Exception("未知错误")).GetDisplay(True), MsgBoxStyle.Critical, "更新失败")
            End If
            Return
        End If

        Try
            FileUtils.Copy(PathExe, NewFilePath)
        Catch ex As UnauthorizedAccessException
            MsgBox(FormatFailure("复制新版本文件时权限不足") & vbCrLf & vbCrLf & "详细错误信息：" & vbCrLf & ex.GetDisplay(True), MsgBoxStyle.Critical, "更新失败")
            Return
        Catch ex As Exception
            MsgBox(FormatFailure("复制新版本文件时出错") & vbCrLf & vbCrLf & "详细错误信息：" & vbCrLf & ex.GetDisplay(True), MsgBoxStyle.Critical, "更新失败")
            Return
        End Try

        If Not TriggerRestart Then Return
        Try
            StartProcess(NewFilePath)
        Catch ex As Exception
            MsgBox("PCL 更新已完成，但在自动重启 PCL 时遇到了意外，请手动启动 PCL。" & vbCrLf & vbCrLf &
                   "详细错误信息：" & vbCrLf & ex.GetDisplay(True), MsgBoxStyle.Information, "更新完成")
        End Try
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Dim LocalVersion = If(FileUtils.Exists(PathTemp & "Latest.exe"), Settings.Get(Of Integer)("SystemHighestSavedBetaVersionReg"), 0)
        Dim ServerVersion = 0
        Dim Notice = If(FileUtils.TryReadAsString(PathTemp & "Cache\Notice.cfg"), "")
        If Notice.Split("|"c).Count >= 3 Then ServerVersion = CInt(Val(Notice.Split("|"c)(2)))
        If LocalVersion < ServerVersion Then
            Logger.Info($"需要下载最新正式版：{LocalVersion} -> {ServerVersion}")
            If ServerConfig Is Nothing Then ServerLoader.WaitForExit(IsForceRestart:=ServerLoader.State = LoadState.Finished)
            If ServerConfig Is Nothing Then Throw New Exception("无法从服务器获取正式版 PCL 下载链接！")
            Dim Urls = CType(ServerConfig("Update")("Release")("DownloadUrls"), JArray).Select(Function(t) t.ToString).ToList()
            NetDownloadByLoader(Urls, PathTemp & "Latest.zip", LoaderToSyncProgress, New FileChecker With {.MinSize = 1024 * 1024})
            FileUtils.ExtractToDirectory(PathTemp & "Latest.zip", PathTemp)
            FileUtils.Delete(PathTemp & "Latest.zip")
            FileUtils.Move(PathTemp & "Plain Craft Launcher 2.exe", PathTemp & "Latest.exe")
            Settings.Set("SystemHighestSavedBetaVersionReg", ServerVersion)
        End If
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", AddressOf ServerSub, Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

    Private Sub ServerSub(Loader As LoaderTask(Of Integer, Integer))
        Try
            If FileUtils.Exists(Paths.Base & "PCL\update.exe") Then
                FileUtils.Delete(Paths.Base & "PCL\update.exe")
                Logger.Info("已清理更新缓存")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "清理更新缓存失败", LogBehavior.ToastIfDebug)
        End Try

        Dim LocalConfigVersion = 0
        Dim ConfigPath = PathTemp & "Cache\ServerConfig.json"
        Try
            If FileUtils.Exists(ConfigPath) Then
                ServerConfig = CType(FileUtils.ReadAsJson(ConfigPath), JObject)
                LocalConfigVersion = CInt(Val(ServerConfig("Version")))
                Logger.Info($"已读取本地缓存的配置：{LocalConfigVersion}")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取配置信息失败", LogBehavior.ToastIfDebug)
            ServerConfig = Nothing
            FileUtils.Delete(ConfigPath)
        End Try
        Loader.Progress = 0.2

        Dim LocalNoticeId = Settings.Get(Of Integer)("HintNotice")
        Dim LocalDownloadId = Settings.Get(Of Integer)("HintDownload")
        Dim NoticePath = PathTemp & "Cache\Notice.cfg"
        Dim NoticeRaw As String
        Dim ServerNoticeId As Integer
        Dim ServerDownloadId As Integer
        Try
            NoticeRaw = NetRequestByClientRetry("https://pcl2-server-1253424809.file.myqcloud.com/notice.cfg{CDN}")
            Dim NoticeParts = NoticeRaw.Split("|"c)
            ServerNoticeId = CInt(Val(NoticeParts(0)))
            ServerDownloadId = CInt(Val(NoticeParts(3)))
            If ServerNoticeId = 0 Then Throw New Exception("获取到的内容有误！（" & NoticeRaw & "）")
            If ServerNoticeId > LocalNoticeId Then
                Logger.Info($"本地公告编号：{LocalNoticeId}，服务器公告：{NoticeRaw}，需更新")
            Else
                Logger.Info($"本地公告编号：{LocalNoticeId}，服务器公告：{NoticeRaw}，无需更新")
                If Settings.Get(Of Integer)("SystemHighestSavedBetaVersionReg") < VersionCode OrElse Not FileUtils.Exists(PathTemp & "Latest.exe") Then
                    Settings.Set("SystemHighestSavedBetaVersionReg", VersionCode)
                    Logger.Info($"复制自身为最新正式版：版本号升高到 {VersionCode}")
                    Try
                        FileUtils.Copy(PathExe, PathTemp & "Latest.exe")
                    Catch ex As Exception
                        Logger.Warn(ex, "复制自身为最新正式版失败", LogBehavior.ToastIfDebug)
                    End Try
                End If
            End If
            FileUtils.Write(NoticePath, NoticeRaw)
        Catch ex As Exception
            If TypeOf ex Is InvalidOperationException AndAlso ex.Message.Contains("FIPS") Then
                If MyMsgBox("由于系统未启用 FIPS 兼容算法，PCL 可能无法正常运行。" & vbCrLf &
                            "请按照教程启用该功能，然后重启 PCL。",
                            "兼容性警告", "打开教程", "取消", "", IsWarn:=True) = 1 Then
                    OpenWebsite("https://blog.csdn.net/qq_37608398/article/details/81209922")
                End If
            Else
                Logger.Warn(ex, "获取 PCL 服务器状态失败", LogBehavior.ToastIfDebug)
                FileUtils.Delete(NoticePath)
            End If
            Return
        End Try
        Loader.Progress = 0.4

        Try
            Dim ServerConfigVersion = Val(NoticeRaw.Split("|"c)(4))
            If LocalConfigVersion < ServerConfigVersion Then
                Logger.Info($"本地配置版本号：{LocalConfigVersion}，服务器配置版本号：{ServerConfigVersion}，需要更新")
                NetDownloadByClient("https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json{CDN}", ConfigPath)
                ServerConfig = CType(FileUtils.ReadAsJson(ConfigPath), JObject)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "下载配置信息失败", LogBehavior.ToastIfDebug)
        End Try
        Loader.Progress = 0.6

        Try
            If ServerDownloadId > LocalDownloadId OrElse Not FileUtils.Exists(PathTemp & "Cache\download.json") Then
                Dim DownloadJson = NetRequestByClientRetry("https://pcl2-server-1253424809.file.myqcloud.com/minecraft/download.json{CDN}", Accept:="*/*", RequireJson:=True)
                FileUtils.Write(PathTemp & "Cache\download.json", DownloadJson)
            End If
            Settings.Set("HintDownload", ServerDownloadId)
        Catch ex As Exception
            Logger.Warn(ex, "下载 PCL 特供版信息失败", LogBehavior.ToastIfDebug)
            FileUtils.Delete(PathTemp & "Cache\download.json")
        End Try
        Loader.Progress = 0.8

        Dim NoticeJson As String
        Try
            If ServerNoticeId <= LocalNoticeId AndAlso FileUtils.Exists(PathTemp & "Cache\Notice.json") Then
                NoticeJson = FileUtils.ReadAsString(PathTemp & "Cache\Notice.json")
            Else
                NoticeJson = NetRequestByClientRetry("https://pcl2-server-1253424809.file.myqcloud.com/notice.json{CDN}", Accept:="*/*", RequireJson:=True)
                FileUtils.Write(PathTemp & "Cache\Notice.json", NoticeJson)
            End If
            Settings.Set("HintNotice", ServerNoticeId)
        Catch ex As Exception
            Logger.Warn(ex, "下载 PCL 服务器公告失败", LogBehavior.ToastIfDebug)
            FileUtils.Delete(PathTemp & "Cache\Notice.json")
            Return
        End Try
        Loader.Progress = 0.95

        Try
            For Each Notice As JObject In CType(NoticeJson.Replace("{UNIQUE}", Identify).DeserializeJson(), JArray)
                Dim NoticeId = If(Notice("id") Is Nothing, 0, CInt(Val(Notice("id"))))
                If NoticeId <= LocalNoticeId Then Continue For
                Dim IsMatched = True
                Dim Requirements = TryCast(Notice("requirements"), JObject)
                If Requirements IsNot Nothing Then
                    For Each Requirement As JProperty In Requirements.Properties()
                        If Not ServerRequirement(Requirement.Name, Requirement.Value) Then IsMatched = False
                    Next
                End If
                If Not IsMatched Then Continue For

                Dim ImportantLevel = If(Notice("importantLevel") Is Nothing, 2, CInt(Notice("importantLevel")))
                Dim IsUpdateNotice = If(Notice("isUpdate") Is Nothing, False, Notice("isUpdate").ToObject(Of Boolean))
                Logger.Info($"重要等级 {ImportantLevel}，更新公告 {IsUpdateNotice}")
                If Settings.Get(Of Integer)(If(IsUpdateNotice, "SystemSystemUpdate", "SystemSystemActivity")) > ImportantLevel OrElse
                   (IsUpdateNotice AndAlso IsUpdating) Then Continue For

                Dim Title = If(Notice("title") Is Nothing, "公告", Notice("title").ToString)
                Dim Description = If(Notice("description") Is Nothing, "", Notice("description").ToString).Replace("\n", vbCrLf)
                Dim Buttons = TryCast(Notice("buttons"), JArray)
                If Buttons Is Nothing Then Buttons = New JArray()
                Dim ActionsToken As JToken = Nothing
                Select Case Buttons.Count
                    Case 0
                        ActionsToken = Notice("actions")
                    Case 1
                        Logger.Info($"显示公告 {NoticeId}：{Description}")
                        MyMsgBox(Description, Title, If(Buttons(0)("text") Is Nothing, "确定", Buttons(0)("text").ToString), "", "", IsWarn:=False, HighLight:=True, ForceWait:=True)
                        ActionsToken = Buttons(0)("actions")
                    Case 2
                        Logger.Info($"显示公告 {NoticeId}：{Description}")
                        Dim Result = MyMsgBox(Description, Title,
                                              If(Buttons(0)("text") Is Nothing, "确定", Buttons(0)("text").ToString),
                                              If(Buttons(1)("text") Is Nothing, "确定", Buttons(1)("text").ToString))
                        Result = Math.Min(Math.Max(Result, 1), Buttons.Count)
                        ActionsToken = Buttons(Result - 1)("actions")
                    Case 3
                        Logger.Info($"显示公告 {NoticeId}：{Description}")
                        Dim Result = MyMsgBox(Description, Title,
                                              If(Buttons(0)("text") Is Nothing, "确定", Buttons(0)("text").ToString),
                                              If(Buttons(1)("text") Is Nothing, "确定", Buttons(1)("text").ToString),
                                              If(Buttons(2)("text") Is Nothing, "确定", Buttons(2)("text").ToString))
                        Result = Math.Min(Math.Max(Result, 1), Buttons.Count)
                        ActionsToken = Buttons(Result - 1)("actions")
                    Case Else
                        Logger.Warn($"公告 {NoticeId} 的弹窗有 {Buttons.Count} 个按钮，无法显示", LogBehavior.ToastIfDebug)
                        Continue For
                End Select

                Dim Actions = TryCast(ActionsToken, JObject)
                If Actions Is Nothing Then Continue For
                For Each ActionEntry As JProperty In Actions.Properties()
                    Try
                        Select Case ActionEntry.Name
                            Case "copy"
                                ClipboardSet(ActionEntry.Value.ToString)
                            Case "website"
                                OpenWebsite(ActionEntry.Value.ToString)
                            Case "stop"
                                FrmMain.EndProgram(SendWarning:=False)
                            Case "setup"
                                For Each SettingEntry As KeyValuePair(Of String, JToken) In CType(ActionEntry.Value, JObject)
                                    Settings.Set(SettingEntry.Key, SettingEntry.Value.ToString)
                                Next
                            Case "update", "slientupdate"
                                Dim SilentUpdate = ActionEntry.Name = "slientupdate"
                                If Not HasUpdatePermission Then
                                    If Not SilentUpdate Then InputPotatoCode(IsUpdating:=True)
                                    If Not HasUpdatePermission Then Continue For
                                End If
                                UpdateStart(SilentUpdate)
                            Case Else
                                Throw New Exception("未知的行动支：" & ActionEntry.Name & ", " & ActionEntry.Value.ToString)
                        End Select
                    Catch ex As Exception
                        Logger.Error(ex, $"执行 PCL 服务器公告动作失败（{ActionEntry.Name}, {ActionEntry.Value}）", LogBehavior.Toast)
                    End Try
                Next
            Next
        Catch ex As Exception
            Logger.Warn(NoticeJson, LogBehavior.ToastIfDebug)
            Logger.Error(ex, $"读取 PCL 服务器公告失败（{NoticeJson.Length}）", LogBehavior.Toast)
            FileUtils.Delete(PathTemp & "Cache\Notice.json")
        End Try
    End Sub

    Private Function ServerRequirement(Name As String, Value As JToken) As Boolean
        Try
            Select Case Name
                Case "d10000 <="
                    Return RandomInteger(1, 10000) <= Val(Value)
                Case "opencount <="
                    Return Settings.Get(Of Integer)("SystemCount") <= Val(Value)
                Case "opencount >="
                    Return Settings.Get(Of Integer)("SystemCount") >= Val(Value)
                Case "versioncode <="
                    Return VersionCode <= Val(Value)
                Case "versioncode >="
                    Return VersionCode >= Val(Value)
                Case "versionbranch <="
                    Return Val(CInt(BuildType)) <= Val(Value)
                Case "versionbranch >="
                    Return Val(CInt(BuildType)) >= Val(Value)
                Case "uniqueaddress ="
                    Return Identify = Value.ToString
                Case "unlockedtheme ="
                    Return ThemeCheckOne(CInt(Val(Value)))
                Case "unlockedtheme !="
                    Return Not ThemeCheckOne(CInt(Val(Value)))
                Case "setupinteger >=", "setupinteger <=", "setupboolean ="
                    Dim SettingRequirement = TryCast(Value, JProperty)
                    If SettingRequirement Is Nothing AndAlso TypeOf Value Is JObject Then SettingRequirement = CType(Value, JObject).Properties().FirstOrDefault()
                    If SettingRequirement Is Nothing Then Throw New Exception("设置条件格式错误：" & Value.ToString)
                    Select Case Name
                        Case "setupinteger >="
                            Return Settings.Get(Of Integer)(SettingRequirement.Name) >= Val(SettingRequirement.Value)
                        Case "setupinteger <="
                            Return Settings.Get(Of Integer)(SettingRequirement.Name) <= Val(SettingRequirement.Value)
                        Case Else
                            Return Settings.Get(Of Boolean)(SettingRequirement.Name) = SettingRequirement.Value.ToObject(Of Boolean)
                    End Select
                Case "debug ="
                    Return ModeDebug.ToString.Lower = Value.ToString.Lower
                Case Else
                    Logger.Warn($"未知的条件支：{Name}, {Value}", LogBehavior.ToastIfDebug)
                    Return False
            End Select
        Catch ex As Exception
            Logger.Warn($"判断分支条件失败：{Name}, {Value}", LogBehavior.ToastIfDebug)
            Return False
        End Try
    End Function

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property
    Private ReadOnly Property HasUpdatePermission As Boolean
        Get
            Return True
        End Get
    End Property

    Private _PotatoFeatures As List(Of String) = Nothing
    Private Const EcdsaPublicKey As String = "RUNTMSAAAAC4QTUNAewh23Q4Q6koHkyIrDIIZUSbua23sf2DiZmIRwSzadISDRyTVTbuWniH3KR7rKj8XBsabms1be6i3c+S"
    Private ReadOnly Property PotatoFeatures As List(Of String)
        Get
            If _PotatoFeatures Is Nothing Then
                _PotatoFeatures = New List(Of String)
                For Each Code In Settings.Get(Of String)("Potatoes").Split("|"c, True)
                    Dim Result = CheckPotatoCode(Code)
                    If Result.Is(Of List(Of String))() Then _PotatoFeatures.AddRange(Result.As(Of List(Of String))())
                Next
                _PotatoFeatures = _PotatoFeatures.Distinct().ToList()
            End If
            Return _PotatoFeatures
        End Get
    End Property

    Private Function CheckPotatoCode(Key As String) As OneOf(Of List(Of String), String)
        If String.IsNullOrEmpty(Key) Then
            Return "输入的土豆码为空。"
        ElseIf Key.Length < 30 AndAlso Key.ContainsIgnoreCase(Identify) Then
            Return $"你输入的是识别码，不是土豆码。{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆 {Identify}】。"
        ElseIf Key.Length > 85 AndAlso Key.Length < 90 AndAlso Key.EndsWithF("=") Then
            Return "你输入的是解锁码，不是土豆码。" & vbCrLf & "要获取土豆码，请在爱发电私信发送【土豆】。"
        ElseIf Key.Length > 150 AndAlso Key.Length < 165 AndAlso Key.EndsWithF("11") Then
            Return "你输入的是更新密钥，不是土豆码。" & vbCrLf & "要获取土豆码，请在爱发电私信发送【土豆】。"
        ElseIf Not Key.StartsWithF("CD", True) OrElse Not Key.Contains("#") Then
            Return $"你输入的不是土豆码……有一说一，这是啥玩意儿？{vbCrLf}要获取土豆码，请在爱发电私信发送【土豆 {Identify}】。"
        ElseIf Key(2) > "1"c Then
            Return "这个土豆码得在新版 PCL 上才能使用。" & vbCrLf & "请更新 PCL，或是在爱发电私信发送【下载】，然后在新下载的 PCL 上输入土豆码。"
        ElseIf "1"c > Key(3) Then
            Return $"这个土豆码不太新鲜，已经过期了。{vbCrLf}请在爱发电私信重新发送【土豆 {Identify}】，获取更新鲜的土豆码。"
        ElseIf Not Key.StartsWithF("CD") Then
            Return "土豆码区分大小写，但你把一些大写的东西输成小写的了。" & vbCrLf & "你可以选中文本，然后右键复制粘贴，不必手动输入。"
        End If

        Try
            Dim Parts = Key.Substring(4).Replace("p", "=").Replace("%", "p").Split("#"c)
            Dim FeaturesText = Encoding.UTF8.GetString(Convert.FromBase64String(Parts(1)))
            CryptographyUtils.EcdsaVerify(Identify & Key(2).ToString & Key(3).ToString & FeaturesText, Parts(0), EcdsaPublicKey)
            Return FeaturesText.Split(","c, True).ToList()
        Catch ex As Exception
            Logger.Warn(ex, $"土豆码校验失败（{Key}）", LogBehavior.ToastIfDebug)
            Return $"土豆码有误，请检查：{vbCrLf}- 私信显示的识别码应当为 {Identify}，如果不是，请在私信发送【土豆 {Identify}】{vbCrLf}- 你输入的以下土豆码是否与私信中的一致：{vbCrLf}{Key}{vbCrLf}{vbCrLf}错误原因：{ex.Message}"
        End Try
    End Function

    Public Sub InputPotatoCode(IsUpdating As Boolean)
        Hint("请使用 PCL 快照版输入土豆码！如果你赞助过 PCL，可以在爱发电私信发送【下载】获取 PCL 快照版！")
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Try
            If ApplicationStartTick < 1 Then Return "0000-0000-0000-0000"

            Dim HardwareId As String
            Try
                HardwareId = My.Computer.Registry.GetValue("HKEY_LOCAL_MACHINE\SYSTEM\HardwareConfig", "LastConfig", "Unknown").ToString.Upper.Trim("{"c, "}"c)
            Catch ex As Exception
                Logger.Warn(ex, "获取主板标识码失败", LogBehavior.ToastIfDebug)
                HardwareId = "Unknown"
            End Try

            Dim SavedId As String
            Try
                SavedId = Settings.Get(Of String)("Identify")
            Catch
                SavedId = ""
            End Try
            If SavedId.Length < 3 Then
                SavedId = GetTimeMs().ToString & My.Computer.Info.AvailablePhysicalMemory.ToString
                Settings.Set("Identify", SavedId)
            End If

            Dim Raw = (HardwareId & SavedId).GetStableHashCode().ToString("X").EnsureLength("7"c, 16)
            Return $"{Raw.Substring(4, 4)}-{Raw.Substring(12, 4)}-{Raw.Substring(0, 4)}-{Raw.Substring(8, 4)}"
        Catch ex As Exception
            Logger.Error(ex, "PCL 无法获取设备识别码，这可能会导致部分设置无法正常存储。" & vbCrLf & vbCrLf & "详细的错误信息", LogBehavior.AlertThenFeedback)
            Return "0000-0000-0000-0000"
        End Try
    End Function

#End Region

End Module
