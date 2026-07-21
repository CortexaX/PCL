Public Module Pcl2Taowa

    Private ReadOnly Gate As New Object
    Private ReadOnly Http As HttpClient = CreateHttpClientNoProxy()
    Private ReadOnly FallbackPublicNodes As String() = {
        "tcp://mc1.easytier.cn:55558",
        "tcp://et.gbc.moe:11010",
        "tcp://sh.vomiku.com:7910",
        "tcp://43.139.242.231:11010"
    }

    Private CachedPublicNodes As String() = Nothing
    Private NodesCachedAt As Date = Date.MinValue
    Private LastError As String = Nothing
    Private LastHostRoom As String = Nothing
    Private InternalStarted As Boolean = False

    Public ReadOnly Property ApiPort As Integer
        Get
            Return 0
        End Get
    End Property

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return InternalStarted
        End Get
    End Property

    Private Function CreateHttpClientNoProxy() As HttpClient
        Return New HttpClient(New HttpClientHandler With {
            .UseProxy = False,
            .Proxy = Nothing,
            .PreAuthenticate = False
        }) With {.Timeout = TimeSpan.FromSeconds(25)}
    End Function

    Private Sub Log(Message As String)
        Try
            Dim LogFolder = Path.Combine(If(AppDomain.CurrentDomain.BaseDirectory, "."), "PCL")
            DirectoryUtils.Create(LogFolder)
            File.AppendAllText(Path.Combine(LogFolder, "TaowaBridge.log"), Date.Now.ToString("HH:mm:ss.fff") & " " & Message & vbCrLf, Encoding.UTF8)
        Catch
        End Try
    End Sub

    Private Function NormalizeRoom(Room As String) As String
        If String.IsNullOrWhiteSpace(Room) Then Return ""
        Return Room.Trim().Replace(" ", "").ToUpperInvariant()
    End Function

    Private Sub RecoverToIdle()
        Try
            Reset()
        Catch
        End Try
    End Sub

    Private Function ResolvePublicNodes() As String()
        If CachedPublicNodes IsNot Nothing AndAlso (Date.UtcNow - NodesCachedAt).TotalMinutes < 30 Then Return CachedPublicNodes
        Dim Nodes As New List(Of String)
        Try
            Dim FetchTask = Http.GetStringAsync("https://terracotta.glavo.site/nodes")
            If FetchTask.Wait(6000) Then
                ExtractUrlsFromJson(FetchTask.Result, Nodes)
                Log("fetched glavo nodes: " & Nodes.Count)
            End If
        Catch ex As Exception
            Log("glavo nodes fail: " & ex.Message)
        End Try
        For Each Node In FallbackPublicNodes
            If Not Nodes.Contains(Node) Then Nodes.Insert(0, Node)
        Next
        CachedPublicNodes = Nodes.
            Where(Function(n) Not String.IsNullOrWhiteSpace(n)).
            Select(Function(n) n.Trim()).
            Distinct(StringComparer.OrdinalIgnoreCase).
            ToArray()
        If CachedPublicNodes.Length = 0 Then CachedPublicNodes = FallbackPublicNodes
        NodesCachedAt = Date.UtcNow
        Return CachedPublicNodes
    End Function

    Private Sub ExtractUrlsFromJson(Json As String, Target As List(Of String))
        If String.IsNullOrEmpty(Json) Then Return
        Try
            Dim Root = JToken.Parse(Json)
            For Each Token In Root.SelectTokens("$..url")
                Dim Value = Token.ToString()
                If Value.StartsWithF("tcp://", True) OrElse Value.StartsWithF("udp://", True) OrElse
                   Value.StartsWithF("ws://", True) OrElse Value.StartsWithF("wss://", True) Then
                    Target.Add(Value)
                End If
            Next
        Catch
        End Try
    End Sub

    Private Function ExceptionHelp(TypeCode As String) As String
        Dim Label = ExceptionLabel(TypeCode)
        Select Case TypeCode
            Case "0"
                Return Label & vbCrLf & vbCrLf &
                    "无法在 EasyTier 网络上找到房主（中继/P2P 失败）。" & vbCrLf & vbCrLf &
                    "请按顺序排查：" & vbCrLf &
                    "1. 关闭 FlClash / Clash 的 TUN 模式后再试" & vbCrLf &
                    "2. 不要在本机加入自己刚开的房间" & vbCrLf &
                    "3. 房主保持 MC「对局域网开放」且不关房间" & vbCrLf &
                    "4. 双方都用最新版本，房主重新开房发新房间码" & vbCrLf &
                    "5. 暂时关闭防火墙试一次"
            Case "1"
                Return Label & vbCrLf & vbCrLf & "主机连接被重置，请让房主重新开房，你再加入。"
            Case "2", "3"
                Return Label & vbCrLf & vbCrLf & "联机核心 EasyTier 崩溃。请关闭代理 TUN 模式后重新创建房间。"
            Case Else
                Return Label & vbCrLf & vbCrLf & "请重试；若反复失败，重启启动器后再试。"
        End Select
    End Function

    Public Sub EnsureStarted()
        Try
            Log("EnsureStarted()")
            SyncLock Gate
                Pcl2TaowaInternal.EnsureInitialized()
                InternalStarted = True
            End SyncLock
            LastError = Nothing
        Catch ex As Exception
            LastError = ex.Message
            InternalStarted = False
            Log("EnsureStarted FAIL: " & ex.ToString())
        End Try
    End Sub

    Private Sub EnsureStartedOrThrow()
        EnsureStarted()
        If Not IsRunning Then Throw New Exception(If(LastError, "陶瓦核心未启动（详见 PCL\TaowaBridge.log）"))
    End Sub

    Public Sub [Stop]()
        SyncLock Gate
            Pcl2TaowaInternal.Reset()
            InternalStarted = False
        End SyncLock
    End Sub

    Public Function GetStateRaw() As String
        Return Pcl2TaowaCore.GetStateRaw()
    End Function

    Private Function StateLabel(State As String) As String
        Select Case If(State, "").ToLowerInvariant()
            Case "waiting" : Return "空闲"
            Case "host-scanning" : Return "扫描局域网中（请在 MC 对局域网开放）"
            Case "host-starting" : Return "正在启动房间"
            Case "host-ok" : Return "开房成功"
            Case "guest-connecting" : Return "正在连接主机"
            Case "guest-starting" : Return "正在建立联机通道"
            Case "guest-ok" : Return "加入成功"
            Case "exception" : Return "异常"
            Case Else : Return If(State, "未知")
        End Select
    End Function

    Private Function ExceptionLabel(TypeCode As String) As String
        Select Case TypeCode
            Case "0" : Return "无法连通主机 (PingHostFail)"
            Case "1" : Return "主机连接被重置 (PingHostRst)"
            Case "2" : Return "访客 EasyTier 崩溃"
            Case "3" : Return "主机 EasyTier 崩溃"
            Case "4" : Return "服务器连接被重置"
            Case "5" : Return "Scaffolding 响应无效"
            Case Else : Return "类型 " & If(TypeCode, "?")
        End Select
    End Function

    Public Sub Host(PlayerName As String)
        Log("Host() queued player=" & PlayerName)
        Hint("陶瓦：正在创建房间…")
        ThreadPool.QueueUserWorkItem(Sub(__) HostWorker(If(String.IsNullOrWhiteSpace(PlayerName), "Player", PlayerName)))
    End Sub

    Private Sub HostWorker(PlayerName As String)
        Try
            EnsureStartedOrThrow()
            Reset()
            Thread.Sleep(300)

            Log("HOST internal")
            Pcl2TaowaInternal.StartHost(PlayerName.Trim(), PublicNodes:=ResolvePublicNodes())

            Hint("已开始扫描 · 已注入中继节点 · 请在 MC 对局域网开放", HintType.Green)
            Dim Room As String = Nothing
            Dim State As String = Nothing
            Dim Watch = Stopwatch.StartNew()
            Dim LastHintSecond = -1
            Do While Watch.Elapsed < TimeSpan.FromSeconds(45)
                Dim RawState = SafeState()
                State = ExtractJsonString(RawState, "state")
                Room = ExtractJsonString(RawState, "room")
                Dim TypeCode = ExtractJsonString(RawState, "type")
                If String.Equals(State, "exception", StringComparison.OrdinalIgnoreCase) Then
                    Hint("开房异常：" & ExceptionLabel(If(TypeCode, "?")), HintType.Red)
                    RecoverToIdle()
                    ShowInfoDialog(ExceptionHelp(If(TypeCode, "?")) & vbCrLf & vbCrLf & "原始状态：" & vbCrLf & RawState, "陶瓦联机 · 开房失败", True)
                    Return
                End If
                If Not String.IsNullOrEmpty(Room) AndAlso
                   (String.Equals(State, "host-ok", StringComparison.OrdinalIgnoreCase) OrElse
                    String.Equals(State, "host-starting", StringComparison.OrdinalIgnoreCase)) Then Exit Do

                Dim Second = CInt(Watch.Elapsed.TotalSeconds)
                If Second >= 3 AndAlso Second \ 3 <> LastHintSecond \ 3 Then
                    LastHintSecond = Second
                    Hint($"等待房间码… {StateLabel(State)} ({Second}s)")
                End If
                Thread.Sleep(400)
            Loop

            If Not String.IsNullOrEmpty(Room) Then
                LastHostRoom = Room
                Dim Label = StateLabel(State)
                Hint($"开房成功 · {Label} · 房间码 {Room}", HintType.Green)
                ShowCopyDialog(
                    "房间已创建成功！" & vbCrLf & vbCrLf &
                    "状态：" & Label & vbCrLf &
                    "房间码：" & vbCrLf & Room & vbCrLf & vbCrLf &
                    "1. 请先在 Minecraft 中「对局域网开放」并保持世界开着" & vbCrLf &
                    "2. 把房间码发给好友" & vbCrLf &
                    "3. 好友在联机页输入房间码加入" & vbCrLf & vbCrLf &
                    "不要在本机再点「加入」；你已经是房主。",
                    "陶瓦联机 · 创建成功", "复制房间码", Room, False)
            Else
                Hint("尚未拿到房间码 · " & StateLabel(State), HintType.Red)
                ShowInfoDialog("已发起开房扫描，但暂未生成房间码。" & vbCrLf & vbCrLf &
                               "当前状态：" & StateLabel(State) & vbCrLf & vbCrLf &
                               "请先启动 Minecraft 并对局域网开放，然后再点一次「创建房间」。" & vbCrLf & vbCrLf &
                               "原始状态：" & vbCrLf & SafeState(),
                               "陶瓦联机 · 等待局域网", False)
            End If
        Catch ex As Exception
            Log("Host FAIL " & ex.ToString())
            Hint("创建房间失败：" & ex.Message, HintType.Red)
            ShowInfoDialog("创建房间失败：" & vbCrLf & ex.Message, "陶瓦联机 · 错误", True)
        End Try
    End Sub

    Public Sub Join(Room As String, PlayerName As String)
        Log("Join() queued room=" & Room)
        Hint("陶瓦：正在加入房间…")
        ThreadPool.QueueUserWorkItem(Sub(__) JoinWorker(Room, If(String.IsNullOrWhiteSpace(PlayerName), "Player", PlayerName)))
    End Sub

    Private Sub JoinWorker(Room As String, PlayerName As String)
        Try
            EnsureStartedOrThrow()
            If String.IsNullOrWhiteSpace(Room) Then Throw New ArgumentException("房间码为空")
            Room = Room.Trim()
            Dim NormalizedRoom = NormalizeRoom(Room)

            Try
                Dim RawState = SafeState()
                Dim CurrentState = If(ExtractJsonString(RawState, "state"), "")
                Dim CurrentRoom = If(ExtractJsonString(RawState, "room"), "")
                If CurrentState.StartsWithF("host-", True) AndAlso NormalizeRoom(CurrentRoom) = NormalizedRoom AndAlso NormalizedRoom.Length > 0 Then
                    Hint("你已是该房间房主，无需加入")
                    ShowInfoDialog("你已经是这个房间的房主，不能在本机再加入。" & vbCrLf & vbCrLf &
                                   "房间码：" & vbCrLf & Room & vbCrLf & vbCrLf &
                                   "请把房间码发给好友，让对方在另一台电脑上加入。",
                                   "陶瓦联机 · 你已是房主", False)
                    Return
                End If
                If Not String.IsNullOrEmpty(LastHostRoom) AndAlso NormalizeRoom(LastHostRoom) = NormalizedRoom Then
                    Hint("不能加入本机创建的房间", HintType.Red)
                    ShowInfoDialog("这是本机创建的房间码，房主端不能再点「加入」。" & vbCrLf & vbCrLf &
                                   "房间码：" & vbCrLf & Room,
                                   "陶瓦联机 · 不能加入自己的房", True)
                    Return
                End If
                If Not String.IsNullOrEmpty(CurrentState) AndAlso Not CurrentState.Equals("waiting", StringComparison.OrdinalIgnoreCase) Then
                    Reset()
                    Thread.Sleep(400)
                End If
            Catch ex As Exception
                Log("Join pre-check: " & ex.Message)
            End Try

            Log("JOIN internal")
            If Not Pcl2TaowaInternal.StartGuest(Room, PlayerName.Trim(), PublicNodes:=ResolvePublicNodes()) Then
                RecoverToIdle()
                Hint("加入失败：房间码格式无效或当前状态不可加入", HintType.Red)
                ShowInfoDialog("加入房间请求失败。" & vbCrLf &
                               "请检查房间码是否完整、格式是否正确，或先重置当前联机状态。",
                               "陶瓦联机 · 错误", True)
                Return
            End If

            Hint("加入请求成功 · 等待地址…", HintType.Green)
            Dim Url As String = Nothing
            Dim State As String = Nothing
            Dim Watch = Stopwatch.StartNew()
            Dim LastHintSecond = -1
            Do While Watch.Elapsed < TimeSpan.FromSeconds(50)
                Dim RawState = SafeState()
                State = ExtractJsonString(RawState, "state")
                Url = ExtractJsonString(RawState, "url")
                Dim TypeCode = ExtractJsonString(RawState, "type")
                If String.Equals(State, "exception", StringComparison.OrdinalIgnoreCase) Then
                    Hint("加入异常：" & ExceptionLabel(If(TypeCode, "?")), HintType.Red)
                    RecoverToIdle()
                    ShowInfoDialog(ExceptionHelp(If(TypeCode, "?")) & vbCrLf & vbCrLf & "原始状态：" & vbCrLf & RawState, "陶瓦联机 · 加入失败", True)
                    Return
                End If
                If Not String.IsNullOrEmpty(Url) AndAlso String.Equals(State, "guest-ok", StringComparison.OrdinalIgnoreCase) Then Exit Do

                Dim Second = CInt(Watch.Elapsed.TotalSeconds)
                If Second >= 3 AndAlso Second \ 3 <> LastHintSecond \ 3 Then
                    LastHintSecond = Second
                    Hint($"加入中… {StateLabel(State)} ({Second}s)")
                End If
                Thread.Sleep(400)
            Loop

            If Not String.IsNullOrEmpty(Url) Then
                Hint("加入成功 · 直连地址 " & Url, HintType.Green)
                ShowCopyDialog("加入房间成功！" & vbCrLf & vbCrLf &
                               "状态：" & StateLabel(State) & vbCrLf &
                               "Minecraft 直连地址：" & vbCrLf & Url & vbCrLf & vbCrLf &
                               "在 Minecraft 中：多人游戏 → 直接连接，粘贴上方地址即可进入。",
                               "陶瓦联机 · 加入成功", "复制地址", Url, False)
            Else
                RecoverToIdle()
                Hint("加入未完成 · " & StateLabel(State), HintType.Red)
                ShowInfoDialog("已发送加入请求，但直连地址尚未就绪。" & vbCrLf & vbCrLf &
                               "当前状态：" & StateLabel(State) & vbCrLf & vbCrLf &
                               "请确认房主仍在线、房间码无误、本机防火墙未拦截联机核心。" & vbCrLf & vbCrLf &
                               "原始状态：" & vbCrLf & SafeState(),
                               "陶瓦联机 · 等待连接", False)
            End If
        Catch ex As Exception
            Log("Join FAIL " & ex.ToString())
            RecoverToIdle()
            Hint("加入失败：" & ex.Message, HintType.Red)
            ShowInfoDialog("加入失败：" & vbCrLf & ex.Message, "陶瓦联机 · 错误", True)
        End Try
    End Sub

    Public Sub Reset()
        Try
            Pcl2TaowaInternal.Reset()
            Log("reset internal idle")
        Catch ex As Exception
            Log("reset fail " & ex.Message)
        End Try
    End Sub

    Public Function TryGetRoomFromState() As String
        Try
            Return ExtractJsonString(GetStateRaw(), "room")
        Catch
            Return Nothing
        End Try
    End Function

    Public Function TryGetUrlFromState() As String
        Try
            Return ExtractJsonString(GetStateRaw(), "url")
        Catch
            Return Nothing
        End Try
    End Function

    Private Function SafeState() As String
        Try
            Return GetStateRaw()
        Catch ex As Exception
            Return "state-error: " & ex.Message
        End Try
    End Function

    Private Function ExtractJsonString(Json As String, Key As String) As String
        Try
            Dim Token = JObject.Parse(Json).SelectToken(Key)
            Return If(Token Is Nothing, Nothing, Token.ToString())
        Catch
            Return Nothing
        End Try
    End Function

    Private Sub ShowCopyDialog(Caption As String, Title As String, CopyLabel As String, CopyText As String, IsWarn As Boolean)
        RunInUi(Sub()
                    MyMsgBox(Caption, Title, CopyLabel, "关闭", IsWarn:=IsWarn,
                             Button1Action:=Sub()
                                                ClipboardSet(CopyText, False)
                                                Hint("已复制！", HintType.Green)
                                            End Sub)
                End Sub)
    End Sub

    Private Sub ShowInfoDialog(Caption As String, Title As String, IsWarn As Boolean)
        RunInUi(Sub() MyMsgBox(Caption, Title, IsWarn:=IsWarn))
    End Sub

End Module
