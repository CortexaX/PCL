Imports System.Runtime.InteropServices
Imports System.Security.Principal

Public Class PageOtherTest

    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Private Structure PrivilegeToken
        Public PrivilegeCount As Integer
        Public Luid As Long
        Public Attributes As Integer
    End Structure

    Private IsLoad As Boolean = False
    Private Shared IsMemoryOptimizeRunning As Boolean = False
    Private IsCaveEnabled As Boolean = True

#Region "初始化"

    Private Sub MeLoaded() Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True

        TextDownloadFolder.Text = Settings.Get(Of String)("CacheDownloadFolder")
        TextDownloadFolder.Validate()
        If Not TextDownloadFolder.IsValidated OrElse TextDownloadFolder.Text = "" Then
            TextDownloadFolder.Text = Paths.Base & "PCL\MyDownload\"
        End If
        TextDownloadFolder.Validate()
        TextDownloadName.Validate()
        StartButtonRefresh()
    End Sub

#End Region

#Region "自定义下载"

    Private Sub SaveCacheDownloadFolder() Handles TextDownloadFolder.ValidatedTextChanged
        Settings.Set("CacheDownloadFolder", TextDownloadFolder.Text)
        CType(TextDownloadName.ValidateRules.First, ValidateFileName).ParentFolder = TextDownloadFolder.Text
        TextDownloadName.Validate()
    End Sub

    Private Sub StartButtonRefresh() Handles TextDownloadUrl.ValidatedTextChanged, TextDownloadFolder.ValidatedTextChanged, TextDownloadName.ValidatedTextChanged
        BtnDownloadStart.IsEnabled = TextDownloadFolder.IsValidated AndAlso TextDownloadUrl.IsValidated AndAlso TextDownloadName.IsValidated
        BtnDownloadOpen.IsEnabled = TextDownloadFolder.IsValidated
    End Sub

    Private Sub TextDownloadUrl_KeyDown(sender As Object, e As KeyEventArgs) Handles TextDownloadUrl.KeyDown, TextDownloadFolder.KeyDown, TextDownloadName.KeyDown
        If e.Key = Key.Enter AndAlso BtnDownloadStart.IsEnabled Then BtnDownloadStart_Click()
    End Sub

    Private Sub TextDownloadUrl_TextChanged() Handles TextDownloadUrl.ValidatedTextChanged
        Try
            If TextDownloadName.Text = "" AndAlso TextDownloadUrl.Text <> "" Then
                TextDownloadName.Text = PathUtils.GetLastPart(WebUtility.UrlDecode(TextDownloadUrl.Text))
            End If
        Catch
        End Try
    End Sub

    Private Sub BtnDownloadStart_Click() Handles BtnDownloadStart.Click
        StartCustomDownload(TextDownloadUrl.Text, TextDownloadName.Text, TextDownloadFolder.Text)
        TextDownloadUrl.Text = ""
        TextDownloadUrl.Validate()
        TextDownloadUrl.ForceShowAsSuccess()
        TextDownloadName.Text = ""
        TextDownloadName.Validate()
        TextDownloadName.ForceShowAsSuccess()
        StartButtonRefresh()
    End Sub

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Try
            If String.IsNullOrWhiteSpace(Folder) Then
                Folder = Dialogs.SaveFile("选择文件保存位置", FileName, filter:=Nothing)
                If Folder Is Nothing Then Return
                If Folder.EndsWithF(FileName) Then Folder = Folder.Substring(0, Folder.Length - FileName.Length)
            End If
            Folder = Folder.Replace("/", "\").TrimEnd("\"c) & "\"

            Try
                DirectoryUtils.Create(Folder)
                CheckPermissionWithException(Folder)
            Catch ex As Exception
                Logger.Error(ex, $"访问文件夹失败（{Folder}）", LogBehavior.Toast)
                Return
            End Try

            Logger.Info($"自定义下载文件名：{FileName}")
            Logger.Info($"自定义下载文件目标：{Folder}")
            Dim Uuid = GetUuid()
            Dim DownloadLoader As New LoaderDownload("自定义下载文件：" & FileName & " ", New List(Of NetFile) From {
                New NetFile({Url}, Folder & FileName, Nothing, SimulateBrowserHeaders:=True)
            })
            Dim Loader As New LoaderCombo(Of Integer)("自定义下载 (" & Uuid & ") ", {DownloadLoader})
            Loader.OnStateChanged = AddressOf DownloadState
            Loader.Start()
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始自定义下载失败", LogBehavior.AlertThenFeedback)
        End Try
    End Sub

    Private Sub BtnDownloadSelect_Click() Handles BtnDownloadSelect.Click
        Dim Folder = Dialogs.SelectFolder("选择目标文件夹", False).FirstOrDefault
        If Folder IsNot Nothing Then TextDownloadFolder.Text = Folder
    End Sub

    Private Sub BtnDownloadOpen_Click() Handles BtnDownloadOpen.Click
        Try
            DirectoryUtils.Create(TextDownloadFolder.Text)
            StartProcess(TextDownloadFolder.Text)
        Catch ex As Exception
            Logger.Warn(ex, "打开下载文件夹失败")
        End Try
    End Sub

    Private Shared Sub DownloadState(Loader As LoaderBase)
        Dim DownloadLoader = CType(Loader, LoaderCombo(Of Integer))
        Select Case DownloadLoader.State
            Case LoadState.Finished
                Hint(DownloadLoader.Name & "完成！", HintType.Green)
                Beep()
            Case LoadState.Failed
                Logger.Error(DownloadLoader.Error, $"{DownloadLoader.Name}失败", LogBehavior.Alert)
                Beep()
            Case LoadState.Canceled
                Hint(DownloadLoader.Name & "已取消！")
        End Select
    End Sub

#End Region

#Region "百宝箱按钮"

    Private Sub BtnJrrp_Click() Handles BtnJrrp.Click
        Jrrp()
    End Sub

    Public Shared Sub Jrrp()
        Dim Raw = CInt(Math.Round(Math.Abs((CDbl(($"asdfgbn{Date.Now.DayOfYear}12#3$45{Date.Now.Year}IUY").GetStableHashCode()) / 3 +
                                            CDbl(($"QWERTY{Identify}0*8&6{Date.Now.Day}kjhg").GetStableHashCode()) / 3) / 527) Mod 1001))
        Dim Value = If(Raw < 970, CInt(Math.Round(Raw / 969 * 99)), 100)
        Dim Comment As String
        If Value = 100 Then
            Comment = "！100！100！！！！！" & If(ThemeUnlock(13, ShowDoubleHint:=False), vbCrLf & "隐藏主题 欧皇彩 已解锁！", "")
        ElseIf Value >= 98 Then
            Comment = "！差点就到 100 了呢……"
        ElseIf Value >= 90 Then
            Comment = "！好评如潮！"
        ElseIf Value >= 65 Then
            Comment = "！今天运气不错呢！"
        ElseIf Value > 50 Then
            Comment = "！还行啦，还行啦。"
        ElseIf Value = 50 Then
            Comment = "！五五开……"
        ElseIf Value >= 40 Then
            Comment = "！勉强还行吧……？"
        ElseIf Value >= 20 Then
            Comment = "！呜……"
        ElseIf Value >= 11 Then
            Comment = "？！不会吧……"
        ElseIf Value >= 1 Then
            Comment = "……（是百分制哦）"
        Else
            Comment = "？！"
            If MyMsgBox("在查看结果前，请先同意以下附加使用条款：" & vbCrLf & vbCrLf &
                        "1. 我知晓并了解 PCL 的今日人品功能完全没有出 Bug。" & vbCrLf &
                        "2. PCL 不对使用本软件所间接造成的一切财产损失（如砸电脑等）等负责。",
                        "今日人品 - 附加使用条款", "同意并查看结果", "再见", IsWarn:=True) = 2 Then Return
        End If

        MyMsgBox(If(FrmMain.IsSystemTimeChanged, "在这个时候，你的人品值会是：", "你今天的人品值是：") & Value & Comment,
                 If(FrmMain.IsSystemTimeChanged, "今日人品? - ", "今日人品 - ") & Date.Now.ToString("yyyy'/'M'/'d"),
                 "我知道了", IsWarn:=Value < 20)
    End Sub

    Private Sub BtnClick_Click() Handles BtnClick.Click
        Try
            Dim Actions As New List(Of Integer) From {RandomInteger(0, 7)}
            If RandomInteger(0, 1) = 1 Then Actions.Add(RandomInteger(1, 6))
            If Date.Now.Month = 4 AndAlso Date.Now.Day = 1 Then Actions = New List(Of Integer) From {7}

            If Actions.Contains(1) Then
                If MyMsgBox("当暴露在点击确定后的场景时，有极小部分人群会引发癫痫。这种情形可能是由于某些未查出的癫痫病症状引起，即使该人员并没有患癫痫病史也有可能造成此类病症。如果您的家人或任何家庭成员曾有过类似症状，请在点击确定前咨询您的医生或医师。如果您在稍后出现任何症状，包括头晕、目眩、眼部或肌肉抽搐、失去意识、失去方向感、抽搐或出现任何自己无法控制的动作，请立即关闭 PCL 并咨询您的医生或医师。" & vbCrLf &
                            "这是最后的警告，是否继续操作？", "警告", "确定", "取消", IsWarn:=True) = 2 Then Return
            Else
                MyMsgBox("PCL 作者不会受理由于点击千万别点造成的任何 Bug。" & vbCrLf &
                         "这是最后的警告，是否继续操作？", "警告", "确定", "确定", "确定", IsWarn:=True)
            End If

            For Each ActionId In Actions
                Select Case ActionId
                    Case 0
                        ThemeDontClick = 1
                        ThemeRefresh()
                    Case 1
                        ThemeDontClick = 2
                        ThemeRefresh()
                    Case 2
                        If FrmMain.PanBack.LayoutTransform IsNot Nothing Then FrmMain.PanBack.RenderTransformOrigin = New Point(1.25, 1.25)
                        Select Case RandomInteger(0, 2)
                            Case 0
                                FrmMain.PanBack.RenderTransform = New ScaleTransform(1, -1)
                            Case 1
                                FrmMain.PanBack.RenderTransform = New ScaleTransform(-1, -1)
                            Case 2
                                FrmMain.PanBack.RenderTransform = New ScaleTransform(-1, 1)
                        End Select
                    Case 3
                        FrmMain.IsSizeSaveable = False
                        FrmMain.PanBack.LayoutTransform = New ScaleTransform(2.5, 2.5)
                        FrmMain.Height = My.Computer.Screen.WorkingArea.Height - 200
                        FrmMain.Width = My.Computer.Screen.WorkingArea.Width - 200
                        FrmMain.Left = 0
                        FrmMain.Top = 0
                        AniStop("Don't Click Scale")
                    Case 4
                        FrmMain.IsSizeSaveable = False
                        FrmMain.RenderTransform = New ScaleTransform()
                        AniStart(AaScaleTransform(FrmMain, -0.85, 5000, 0, New AniEaseOutFluent(AniEasePower.ExtraStrong)), "Don't Click Scale")
                        AniStop("Don't Click Rotate")
                    Case 5
                        FrmMain.RenderTransform = New RotateTransform()
                        AniStart(AaRotateTransform(FrmMain, 1000000 * RandomOne(New Integer() {1, -1}), 10000000), "Don't Click Rotate")
                        AniStop("Don't Click Scale")
                    Case 6
                        FrmMain.IsSizeSaveable = False
                        If RandomInteger(0, 1) = 0 Then
                            AniStart(AaY(FrmMain, 10000000 * RandomOne(New Integer() {1, -1}), 50000000), "Don't Click Move Y")
                        Else
                            AniStart(AaX(FrmMain, 10000000 * RandomOne(New Integer() {1, -1}), 30000000), "Don't Click Move X")
                        End If
                        RunInThread(Sub()
                                        Do While True
                                            RunInUi(Sub()
                                                        If FrmMain.Top < 0 - FrmMain.Height Then FrmMain.Top = My.Computer.Screen.Bounds.Height + 49
                                                        If FrmMain.Top > My.Computer.Screen.Bounds.Height + 50 Then FrmMain.Top = 0 - FrmMain.Height + 1
                                                        If FrmMain.Left < 0 - FrmMain.Width Then FrmMain.Left = My.Computer.Screen.Bounds.Width + 49
                                                        If FrmMain.Left > My.Computer.Screen.Bounds.Width + 50 Then FrmMain.Left = 0 - FrmMain.Width + 1
                                                    End Sub)
                                            Thread.Sleep(10)
                                        Loop
                                    End Sub)
                    Case 7
                        OpenWebsite("https://www.bilibili.com/video/BV1GJ411x7h7")
                End Select
            Next
            BtnClick.Visibility = Visibility.Collapsed
        Catch
        End Try
    End Sub

#End Region

#Region "垃圾清理"

    Private Sub BtnClear_Click() Handles BtnClear.Click
        RubbishClear()
    End Sub

    Public Shared Sub RubbishClear()
        RunInUi(Sub()
                    If FrmOtherTest IsNot Nothing AndAlso FrmOtherTest.BtnClear IsNot Nothing AndAlso FrmOtherTest.BtnClear.IsLoaded Then
                        FrmOtherTest.BtnClear.IsEnabled = False
                    End If
                End Sub)
        RunInNewThread(Sub()
                           Try
                               If ModWatcher.HasRunningMinecraft OrElse McLaunchLoader.State = LoadState.Loading Then
                                   Hint("请先关闭所有运行中的游戏！")
                                   Return
                               End If
                               If HasDownloadingTask() Then
                                   Hint("请在所有下载任务完成后再来清理吧！")
                                   Return
                               End If

                               If Not McFolderList.Any Then McFolderListLoader.Start()
                               If PathTemp = Path.GetTempPath() & "PCL\" Then
                                   If Settings.Get(Of Integer)("HintClearRubbish") <= 2 Then
                                       If MyMsgBox("即将清理游戏日志、错误报告、缓存等文件。" & vbCrLf &
                                                   "虽然应该没人往这些地方放重要文件，但还是问一下，是否确认继续？" & vbCrLf & vbCrLf &
                                                   "在完成清理后，需要重启 PCL。", "清理确认", "确定", "取消") = 2 Then Return
                                       Settings.Set("HintClearRubbish", Settings.Get(Of Integer)("HintClearRubbish") + 1)
                                   End If
                               Else
                                   If MyMsgBox("即将清理游戏日志、错误报告、缓存等文件。" & vbCrLf & vbCrLf &
                                               "你已将缓存文件夹手动修改为：" & PathTemp & vbCrLf &
                                               "清理过程中，将删除该文件夹中的所有内容，且无法恢复。请确认其中没有除了 PCL 缓存以外的重要文件！" & vbCrLf & vbCrLf &
                                               "在完成清理后，需要重启 PCL。", "清理确认", "确定", "取消") = 2 Then Return
                               End If

                               Dim Folders As New List(Of String)
                               If Not McFolderList.Any Then McFolderListLoader.WaitForExit()
                               For Each Folder In McFolderList
                                   Folders.Add(Folder.Location)
                                   Folders.AddRange(DirectoryUtils.EnumerateDirectories(Folder.Location & "versions", False, "*"))
                               Next

                               Dim DeleteFolder As Action(Of String) =
                                   Sub(DirPath As String)
                                       Try
                                           DirectoryUtils.Delete(DirPath, False)
                                       Catch ex As Exception
                                           Logger.Warn(ex, $"清理文件夹失败（{DirPath}）")
                                       End Try
                                   End Sub

                               For Each Folder In Folders
                                   DeleteFolder(Path.Combine(Folder, ".mixin.out"))
                                   DeleteFolder(Path.Combine(Folder, "crash-reports"))
                                   DeleteFolder(Path.Combine(Folder, "debug"))
                                   DeleteFolder(Path.Combine(Folder, "logs"))
                                   For Each File In DirectoryUtils.EnumerateFiles(Folder, False, "*")
                                       Dim Name = PathUtils.GetLastPart(File)
                                       If Name.StartsWithF("hs_err_pid") OrElse Name.EndsWithF(".log") OrElse Name = "WailaErrorOutput.txt" Then
                                           FileUtils.Delete(File, False)
                                       End If
                                   Next
                                   For Each SubFolder In DirectoryUtils.EnumerateDirectories(Folder, False, "*")
                                       Dim Name = PathUtils.GetLastPart(SubFolder)
                                       If Name = PathUtils.GetLastPart(Folder) & "-natives" OrElse Name = "natives-windows-x86_64" Then
                                           DeleteFolder(SubFolder)
                                       End If
                                   Next
                               Next

                               DeleteFolder(PathTemp)
                               DeleteFolder(OsDrive & "ProgramData\PCL\")
                               MyMsgBox("垃圾已经清理好啦！", "清理完成", "重启 PCL", HighLight:=True, ForceWait:=True)
                               StartProcess(New ProcessStartInfo(PathExe, "--wait"))
                               FormMain.EndProgramForce()
                           Catch ex As Exception
                               Logger.Error(ex, "清理垃圾失败", LogBehavior.Toast)
                           Finally
                               RunInUiWait(Sub()
                                               If FrmOtherTest IsNot Nothing AndAlso FrmOtherTest.BtnClear IsNot Nothing AndAlso FrmOtherTest.BtnClear.IsLoaded Then
                                                   FrmOtherTest.BtnClear.IsEnabled = True
                                               End If
                                           End Sub)
                           End Try
                       End Sub, "Rubbish Clear")
    End Sub

#End Region

#Region "内存优化"

    Private Sub BtnMemory_Click() Handles BtnMemory.Click
        RunInThread(Sub() MemoryOptimize(True))
    End Sub

    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        If IsMemoryOptimizeRunning Then
            If ShowHint Then Hint("内存优化尚未结束，请稍等！")
            Return
        End If
        IsMemoryOptimizeRunning = True

        Dim Value As Long
        If WindowsUtils.HasAdminRole() Then
            Dim OldMemory = CDec(My.Computer.Info.AvailablePhysicalMemory)
            Try
                MemoryOptimizeInternal(ShowHint)
            Catch ex As Exception
                Logger.Warn(ex, "内存优化失败", If(ShowHint, LogBehavior.Toast, LogBehavior.ToastIfDebug))
                Return
            Finally
                IsMemoryOptimizeRunning = False
            End Try
            Value = CLng(CDec(My.Computer.Info.AvailablePhysicalMemory) - OldMemory)
        Else
            Logger.Info("没有管理员权限，将以命令行方式进行内存优化")
            Try
                Value = CLng(RunAsAdmin("--memory")) * 1024L
            Catch ex As Exception
                Logger.Warn(ex, "命令行形式内存优化失败")
                If ShowHint Then Hint($"获取管理员权限失败，请尝试右键 PCL，选择 {vbLQ}以管理员身份运行{vbRQ}！", HintType.Red)
                Return
            Finally
                IsMemoryOptimizeRunning = False
            End Try
            If Value < 0 Then Return
        End If

        Dim LeftMemory = StringUtils.FormatByteSize(CLng(My.Computer.Info.AvailablePhysicalMemory))
        Logger.Info($"内存优化完成，可用内存改变量：{StringUtils.FormatByteSize(Value)}，大致剩余内存：{LeftMemory}")
        If Value > 0 Then
            If ShowHint Then Hint($"内存优化完成，可用内存增加了 {StringUtils.FormatByteSize(CLng(Math.Round(Value * 0.8)))}，目前剩余内存 {LeftMemory}！", HintType.Green)
        ElseIf ShowHint Then
            Hint($"内存优化完成，已经优化到了最佳状态，目前剩余内存 {LeftMemory}！")
        End If
    End Sub

    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        If Not WindowsUtils.HasAdminRole() Then
            Throw New Exception("内存优化功能需要管理员权限！" & vbCrLf &
                                "如果需要自动以管理员身份启动 PCL，可以右键 PCL，打开 属性 → 兼容性 → 以管理员身份运行此程序。")
        End If

        Logger.Info("获取内存优化权限")
        Using Identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query Or TokenAccessLevels.AdjustPrivileges)
            Dim NewState As New PrivilegeToken With {.PrivilegeCount = 1, .Luid = 0, .Attributes = 2}
            Dim SystemName As String = Nothing
            Dim PrivilegeName As String = "SeProfileSingleProcessPrivilege"
            Dim PreviousState = IntPtr.Zero
            Dim ReturnLength = 0
            If Not LookupPrivilegeValue(SystemName, PrivilegeName, NewState.Luid) OrElse
               Not AdjustTokenPrivileges(Identity.Token, False, NewState, Marshal.SizeOf(NewState), PreviousState, ReturnLength) OrElse
               Marshal.GetLastWin32Error() <> 0 Then
                Throw New Exception($"获取内存优化权限失败（错误代码：{Marshal.GetLastWin32Error()}）")
            End If
        End Using

        If ShowHint Then Hint("正在进行内存优化……")
        For TypeId = 2 To 4
            Logger.Info($"内存优化操作 {TypeId} 开始")
            Dim Handle = GCHandle.Alloc(TypeId, GCHandleType.Pinned)
            Dim Result = CInt(NtSetSystemInformation(80, Handle.AddrOfPinnedObject(), Marshal.SizeOf(TypeId)))
            Handle.Free()
            If Result <> 0 Then Throw New Exception($"内存优化操作 {TypeId} 失败（错误代码：{Result}）")
        Next
    End Sub

    <DllImport("ntdll.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function NtSetSystemInformation(SystemInformationClass As Integer, SystemInformation As IntPtr, SystemInformationLength As Integer) As UInteger
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Ansi, EntryPoint:="LookupPrivilegeValueA", ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function LookupPrivilegeValue(<MarshalAs(UnmanagedType.VBByRefStr)> ByRef lpSystemName As String,
                                                 <MarshalAs(UnmanagedType.VBByRefStr)> ByRef lpName As String,
                                                 ByRef lpLuid As Long) As Boolean
    End Function

    <DllImport("advapi32.dll", CharSet:=CharSet.Ansi, ExactSpelling:=True, SetLastError:=True)>
    Private Shared Function AdjustTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean,
                                                  ByRef NewState As PrivilegeToken, BufferLength As Integer,
                                                  ByRef PreviousState As IntPtr, ByRef ReturnLength As Integer) As Boolean
    End Function

#End Region

#Region "回声洞"

    Private Sub BtnCave_Click() Handles BtnCave.Click
        OpenWebsite("https://jinshuju.net/f/esXHQF")
    End Sub

    Private Sub CaveHand() Handles BtnCave.Loaded
        BtnCave.Visibility = If(CurrentRank < DonationRank.Rank23, Visibility.Collapsed, Visibility.Visible)
    End Sub

    Private Sub CardCave_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CardCave.MouseLeftButtonUp
        If Not IsCaveEnabled Then Return
        Dim NewText = GetRandomCave()
        Dim OldText = LabCave.Text
        LabCave.Text = NewText
        IsCaveEnabled = False
        AniStart({
            AaOpacity(LabCave, -1, 5),
            AaOpacity(LabCave, 1, 5, 30),
            AaOpacity(LabCave, -1, 5, 70),
            AaOpacity(LabCave, 0.9, 5, 85),
            AaOpacity(LabCave, -1, 5, 125),
            AaOpacity(LabCave, 0.8, 5, 145),
            AaOpacity(LabCave, -1, 5, 165),
            AaOpacity(LabCave, 0.7, 5, 195),
            AaOpacity(LabCave, -1, 5, 210),
            AaOpacity(LabCave, 0.5, 5, 235),
            AaOpacity(LabCave, -1, 5, 250),
            AaOpacity(LabCave, 1, 1, 400, After:=True),
            AaTextAppear(LabCave, Hide:=False, TimePerText:=True, Time:=60, Delay:=400),
            AaCode(Sub() IsCaveEnabled = True, After:=True)
        }, "Cave")
        LabCave.Text = OldText
    End Sub

    Public Shared Function GetRandomCave() As String
        Return RandomOne(New String() {
            RandomOne(New String() {
                "来硬的！", "钻石！", "我们需要再深入些！", "结束了？", "见鬼去吧！", "君临天下！", "与火共舞！", "本地的酿造厂！", "为什么会变成这样呢？", "信标工程师！",
                "不稳定的同盟！", "天空即为极限！", "甜蜜的梦！", "探索的时光！", "狙击手的对决！", "这是？工作台！", "永恒的伙伴！", "腥味十足的生意！", "开始了？", "这交易不错！",
                "你的世界！", "/summon Creeper ~ ~ ~ {Fuse:0}", "MC-98587!", "紫黑格子波纹疾走！", "命令方块不适合作为武器！", "新增了一堆 Bug！", "Also try Create!", "这是刻意的游戏设计！", "你好中国！", "/give @a hugs 64",
                "Minecraft Legend!", "Creeper?", "Minecraft 2.0!", "Hello, Herobrine!", "It's a FEATURE! Not a BUG!", "我 Mojang 绝不跳票！", "苦力怕！不是爬行者！", "比钻石更强！", "BUGJANG!", "猪灵劲曲！"
            }),
            RandomOne(New String() {
                "希望这个游戏可以给你带来更多的快乐！", "Also try Celeste!", "Also try The Witness!", "人生没有标准答案，去做自己认为正确的事！", "FLAG IS WIN!", "凌波微步，快乐的舞步！", "这个叫 TAS 的人是不是开挂了？", "0 errors, 0 warnings!", "开学愉快！", "Long may the sunshine!",
                "If you can.", "Tell me your secret.", "再次点击这里以查看更多的 PCL 作者和各位沙雕网友乱七八糟的留言！", "你每天都会忘掉很多事，为什么不把这件事也忘掉呢？", "点击千万别点，会送正版哦！", "今日人品？", "这里没有人~我们都是鬼~", "TECHNOBLADE YOU NERDSSSSSSS!", "Bug 变 Feature，妙啊！", "听我说谢谢你~因为有你~温暖了四季~"
            }),
            RandomOne(New String() {
                "在 MC 中，沙子可以下落，说明 MC 还是很科学的！（确信", "唔咕，要饭大失败……（眼神死", "你蓝了，你白了，你没了！", "这是回声洞~是回声洞~回声洞~声洞~洞~", "往往结束才是开始！", "检测到 Minecraft 进程意外退出，错误分析已开始……", "玻璃，放错，退游，一气呵成！", "你已被服务器封禁！理由：请自证 1145 CPS！", "生活枯燥无味，龙猫模仿人类！", "The cake is a lie.",
                "别戳了，这里没有你想找的东西！", "Make Minecraft Great Again!", "Minecraft 1.7.10 - 单人游戏（未响应）", "* 移除了 Herobrine\n* 修复了一个 Bug\n* 增加了一个 Bug", "Plain Craft Launcher 的中文翻译是：普通飞行器发射器！", "We are the universe. We are everything you think isn't you.\n——终末之诗", "下降率大点没事！", "众所周知，Bug 修掉一个还会有第二个 Bug 伴随着修掉的 Bug 出现！", "Click Circle!", "Hex Dragon！"
            }),
            GetRandomPresetHint()
        }).Replace("\n", vbCrLf)
    End Function

    Public Shared Function GetRandomHint() As String
        Try
            If FileUtils.Exists(Paths.Base & "PCL\hints.txt") Then
                Dim Hints = FileUtils.ReadAsLines(Paths.Base & "PCL\hints.txt").Where(Function(s) Not String.IsNullOrWhiteSpace(s)).ToList
                If Hints.Any Then Return RandomOne(Hints)
            End If
        Catch ex As Exception
            Logger.Error(ex, "获取自定义 你知道吗 提示失败", LogBehavior.Toast)
        End Try
        Return GetRandomPresetHint()
    End Function

    Public Shared Function GetRandomPresetHint() As String
        Return RandomOne(New String() {
            "在版本选择页面，右键某个版本也能进入版本设置页面！",
            "你可以在版本设置中调整分类，以将特定版本隐藏！",
            "使用 --memory 参数启动 PCL 可以静默进行内存优化！",
            "在第一次启动游戏时，PCL 会自动将语言设置为中文！",
            "在版本设置中可以开启第三方登录验证，例如 Little Skin 登录！",
            "自动配置游戏内存时，PCL 将会根据剩余内存与 Mod 数量动态决定分配的内存！",
            "主页可以使用特定的预设，在设置中看看吧！",
            "在高级启动选项中，可以设置在游戏启动前运行特定程序！",
            "将鼠标悬浮在设置页的左边栏，可以找到重置设置按钮！",
            "版本设置只对当前版本生效，而设置页面的设置对所有版本生效！",
            $"要将已有的 MC 文件夹加入 PCL，可以在版本选择页的左侧选择 {vbLQ}添加已有文件夹{vbRQ}！",
            "如果同时安装了 OptiFine 与对应的原版，PCL 会展示 OptiFine 版本，折叠原版！",
            "版本选择的 常规版本 分类中，只会列出最新的一个快照或预发布版。",
            "在版本选择页面，右键游戏文件夹可以进行打开、重命名、删除等操作！",
            $"如果你在其他地方修改了皮肤，需要手动选择 {vbLQ}刷新皮肤{vbRQ} 才能更新登录页面的头像……",
            "下载 Mod 时，PCL 会自动定位对应版本的 Mod 文件夹！",
            "如果缺少 Java，PCL 也能自动下载，不必自己安装啦！",
            "如果你打开了调试模式，启动页右侧就会显示启动日志！",
            "将鼠标悬浮在下载页的左边栏，可以找到刷新按钮！",
            $"将鼠标指向下载页的 MC 版本，可以在右侧找到 {vbLQ}更新日志{vbRQ} 选项！",
            "直接把 Mod 或整合包拖进 PCL 窗口就能安装了！",
            $"在 PCL 文件夹下新建 hints.txt，可以自定义 {vbLQ}你知道吗{vbRQ} 的内容！",
            "设置中可以自定义离线皮肤，但这只对单人游戏有效！",
            "如果不想用某项功能，可以在个性化设置中把它隐藏掉！",
            $"如果打开了 {vbLQ}游戏更新提示{vbRQ} 功能，当 MC 更新时 PCL 会弹窗进行提醒！",
            "你可以使用主页来自定义快捷方式！",
            "点击版本右侧的心形就能将该版本加入收藏夹，便于查找。",
            "PCL 的第一个内部版本制作于 2018 年 8 月 13 日！",
            "据调查，有 90.3% 的用户点击了百宝箱中的千万别点按钮！",
            "PCL 的绝大多数代码都在 GitHub 开源了！",
            "PCL 的开发者龙腾猫跃经常被简称为龙猫，但和那只龙猫没有任何关系！"
        })
    End Function

#End Region

End Class
