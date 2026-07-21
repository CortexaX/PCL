Public Module Pcl2TaowaInternal

    Private ReadOnly Gate As New Object
    Private ScaffoldingServer As TaowaScaffoldingServer = Nothing

    Public Sub EnsureInitialized()
        SyncLock Gate
            If ScaffoldingServer Is Nothing Then
                ScaffoldingServer = TaowaScaffoldingServer.StartDefault(Pcl2TaowaCore.CreateDefaultScaffoldingHandlers())
                Log("Scaffolding server started at " & ScaffoldingServer.Port)
            End If
        End SyncLock
    End Sub

    Public Sub Reset()
        Pcl2TaowaCore.SetWaiting()
    End Sub

    Public Sub StartHost(PlayerName As String, Optional RoomCode As String = Nothing, Optional PublicNodes As IEnumerable(Of String) = Nothing)
        ThreadPool.QueueUserWorkItem(Sub(__) HostWorker(If(String.IsNullOrWhiteSpace(PlayerName), "Player", PlayerName), RoomCode, PublicNodes))
    End Sub

    Public Function StartGuest(RoomCode As String, PlayerName As String, Optional PublicNodes As IEnumerable(Of String) = Nothing) As Boolean
        Dim Room = Pcl2TaowaCore.ParseRoom(RoomCode)
        If Room Is Nothing Then Return False

        Dim Capture = Pcl2TaowaCore.SetGuestConnecting(Room)
        If Capture Is Nothing Then Return False

        ThreadPool.QueueUserWorkItem(Sub(__) GuestWorker(Room, If(String.IsNullOrWhiteSpace(PlayerName), "Player", PlayerName), PublicNodes, Capture))
        Return True
    End Function

    Private Sub HostWorker(PlayerName As String, RoomCode As String, PublicNodes As IEnumerable(Of String))
        Dim Capture As TaowaStateCapture = Nothing
        Try
            EnsureInitialized()

            Dim Scanner = TaowaMinecraftScanner.Create(Function(Motd) Not String.Equals(Motd, Pcl2TaowaCore.TaowaMotd, StringComparison.Ordinal))
            Capture = Pcl2TaowaCore.SetHostScanning(Scanner)
            If Capture Is Nothing Then
                Scanner.Dispose()
                Return
            End If

            Dim Room = If(Pcl2TaowaCore.ParseRoom(RoomCode), Pcl2TaowaCore.CreateRoom())
            Dim MinecraftPort = WaitForMinecraftLanPort(Scanner, Capture)
            If MinecraftPort <= 0 Then Return

            Capture = Pcl2TaowaCore.SetHostStarting(Room, MinecraftPort, Capture)
            If Capture Is Nothing Then Return

            Dim Arguments = Pcl2TaowaEasyTier.BuildScaffoldingHostArguments(Room, MergePublicNodes(PublicNodes), ScaffoldingServer.Port, MinecraftPort)
            Dim EasyTier = TaowaEasyTierProcess.Create(Arguments)
            Dim HostProfile = Pcl2TaowaCore.CreateLocalProfile(PlayerName, TaowaProfileKind.HOST)

            Dim HostCapture = Pcl2TaowaCore.SetHostOk(Room, MinecraftPort, New TaowaProfile() {HostProfile}, Capture, EasyTier)
            If HostCapture Is Nothing Then
                EasyTier.Dispose()
                Return
            End If

            ThreadPool.QueueUserWorkItem(Sub(__) HostMonitor(MinecraftPort, EasyTier, HostCapture))
        Catch ex As Exception
            Log("HostWorker failed: " & ex.ToString())
            Pcl2TaowaCore.SetException(TaowaExceptionType.HostEasytierCrash, Capture)
        End Try
    End Sub

    Private Function WaitForMinecraftLanPort(Scanner As TaowaMinecraftScanner, Capture As TaowaStateCapture) As Integer
        Do
            Thread.Sleep(200)
            If Not Pcl2TaowaCore.CanCapture(Capture) Then Return 0

            Dim Ports = Scanner.GetPorts()
            If Ports IsNot Nothing AndAlso Ports.Count > 0 Then Return Ports(0)
        Loop
    End Function

    Private Sub HostMonitor(MinecraftPort As Integer, EasyTier As TaowaEasyTierProcess, Capture As TaowaStateCapture)
        Dim FailedPings = 0
        Do
            Thread.Sleep(5000)

            If Pcl2TaowaCore.CheckMinecraftConnection(MinecraftPort) Then
                FailedPings = 0
            Else
                FailedPings += 1
                If FailedPings >= 3 Then
                    Pcl2TaowaCore.SetException(TaowaExceptionType.PingServerRst, Capture)
                    Return
                End If
            End If

            If Not EasyTier.IsAlive Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.HostEasytierCrash, Capture)
                Return
            End If

            If Not Pcl2TaowaCore.PruneHostProfiles(Capture) Then Return
        Loop
    End Sub

    Private Sub GuestWorker(Room As TaowaRoom, PlayerName As String, PublicNodes As IEnumerable(Of String), Capture As TaowaStateCapture)
        Dim EasyTier As TaowaEasyTierProcess = Nothing
        Dim Session As TaowaScaffoldingClientSession = Nothing
        Try
            EnsureInitialized()

            EasyTier = TaowaEasyTierProcess.Create(Pcl2TaowaEasyTier.BuildScaffoldingGuestArguments(Room, MergePublicNodes(PublicNodes)))
            Capture = Pcl2TaowaCore.SetGuestStarting(Room, TaowaConnectionDifficulty.Unknown, Capture, EasyTier)
            If Capture Is Nothing Then
                EasyTier.Dispose()
                Return
            End If

            Dim Scaffolding = ResolveScaffoldingServer(EasyTier, Capture)
            If Scaffolding Is Nothing Then Return

            Session = OpenVerifiedScaffoldingSession(Scaffolding.LocalPort, EasyTier, Capture)
            If Session Is Nothing Then Return

            Dim MinecraftPort = ReadMinecraftServerPort(Session, Capture)
            If MinecraftPort <= 0 Then
                Session.Dispose()
                Return
            End If

            Dim LocalPort = CreateMinecraftForward(EasyTier, Scaffolding.HostAddress, MinecraftPort, Capture)
            If LocalPort <= 0 Then
                Session.Dispose()
                Return
            End If

            For i = 0 To 7
                If Pcl2TaowaCore.CheckMinecraftConnection(LocalPort) Then Exit For
            Next

            Dim LocalProfile = Pcl2TaowaCore.CreateLocalProfile(PlayerName, TaowaProfileKind.LOCAL)
            Dim FakeServer = TaowaMinecraftFakeServer.Create(LocalPort)
            Dim GuestCapture = Pcl2TaowaCore.SetGuestOk(Room, LocalPort, New TaowaProfile() {LocalProfile}, Capture, EasyTier, FakeServer)
            If GuestCapture Is Nothing Then
                Session.Dispose()
                FakeServer.Dispose()
                EasyTier.Dispose()
                Return
            End If

            ThreadPool.QueueUserWorkItem(Sub(__) GuestProfileLoop(Session, LocalProfile, EasyTier, GuestCapture))
        Catch ex As Exception
            Log("GuestWorker failed: " & ex.ToString())
            If Session IsNot Nothing Then Session.Dispose()
            If EasyTier IsNot Nothing Then EasyTier.Dispose()
            Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
        End Try
    End Sub

    Private Function ResolveScaffoldingServer(EasyTier As TaowaEasyTierProcess, Capture As TaowaStateCapture) As TaowaResolvedScaffolding
        For i = 0 To 4
            Thread.Sleep(3000)
            If Not Pcl2TaowaCore.CanCapture(Capture) Then Return Nothing
            If Not EasyTier.IsAlive Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
                Return Nothing
            End If

            Dim Players = EasyTier.GetPlayers()
            If Players Is Nothing Then Continue For

            Dim LocalNat As TaowaNatType? = Nothing
            For Each Player In Players
                If Player.IsLocal Then
                    LocalNat = Player.Nat
                    Exit For
                End If
            Next
            If Not LocalNat.HasValue Then Continue For

            Dim HostAddress As IPAddress = Nothing
            Dim HostPort = 0
            Dim HostNat As TaowaNatType? = Nothing
            For Each Player In Players
                Dim Prefix = "scaffolding-mc-server-"
                If Not Player.IsLocal AndAlso Player.Address IsNot Nothing AndAlso Player.Hostname.StartsWith(Prefix, StringComparison.Ordinal) Then
                    If Integer.TryParse(Player.Hostname.Substring(Prefix.Length), HostPort) Then
                        HostAddress = Player.Address
                        HostNat = Player.Nat
                        Exit For
                    End If
                End If
            Next
            If HostAddress Is Nothing OrElse Not HostNat.HasValue Then Continue For

            Dim LocalPort = Pcl2TaowaEasyTier.RequestPort(TaowaPortRequest.Scaffolding)
            Dim Forward = New TaowaEasyTierPortForward(New IPEndPoint(IPAddress.Any, LocalPort), New IPEndPoint(HostAddress, HostPort), TaowaEasyTierProtocol.TCP)
            If Not EasyTier.AddPortForward(New TaowaEasyTierPortForward() {Forward}) Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
                Return Nothing
            End If

            Pcl2TaowaCore.SetGuestDifficulty(Pcl2TaowaEasyTier.CalculateConnectionDifficulty(LocalNat.Value, HostNat.Value), Capture)
            Return New TaowaResolvedScaffolding(LocalPort, HostAddress)
        Next

        Pcl2TaowaCore.SetException(TaowaExceptionType.PingHostFail, Capture)
        Return Nothing
    End Function

    Private Function OpenVerifiedScaffoldingSession(LocalPort As Integer, EasyTier As TaowaEasyTierProcess, Capture As TaowaStateCapture) As TaowaScaffoldingClientSession
        Dim Fingerprint = New Byte() {&H41, &H57, &H48, &H44, &H86, &H37, &H40, &H59, &H57, &H44, &H92, &H43, &H96, &H99, &H85, &H1}
        For i = 0 To 59
            Thread.Sleep(4000)

            Try
                Dim Session = TaowaScaffoldingClientSession.Open(IPAddress.Loopback, LocalPort)
                Dim Response = Session.SendSync("c", "ping", Fingerprint)
                If Response IsNot Nothing AndAlso Response.Data.Length = Fingerprint.Length AndAlso Response.Data.SequenceEqual(Fingerprint) Then
                    Return Session
                End If
                Session.Dispose()
            Catch
            End Try

            If Not Pcl2TaowaCore.CanCapture(Capture) Then Return Nothing
            If Not EasyTier.IsAlive Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
                Return Nothing
            End If
        Next

        Pcl2TaowaCore.SetException(TaowaExceptionType.PingHostFail, Capture)
        Return Nothing
    End Function

    Private Function ReadMinecraftServerPort(Session As TaowaScaffoldingClientSession, Capture As TaowaStateCapture) As Integer
        Dim Response = Session.SendSync("c", "server_port")
        If Response Is Nothing OrElse Response.Data.Length <> 2 Then
            Pcl2TaowaCore.SetException(TaowaExceptionType.PingHostFail, Capture)
            Return 0
        End If
        Return (CInt(Response.Data(0)) << 8) Or CInt(Response.Data(1))
    End Function

    Private Function CreateMinecraftForward(EasyTier As TaowaEasyTierProcess, HostAddress As IPAddress, MinecraftPort As Integer, Capture As TaowaStateCapture) As Integer
        Dim Specific = Pcl2TaowaEasyTier.RequestSpecificPort(MinecraftPort)
        Dim LocalPort = If(Specific.HasValue, Specific.Value, Pcl2TaowaEasyTier.RequestPort(TaowaPortRequest.Minecraft))
        Dim Remote = New IPEndPoint(HostAddress, MinecraftPort)
        Dim Forwards = {
            New TaowaEasyTierPortForward(New IPEndPoint(IPAddress.Any, LocalPort), Remote, TaowaEasyTierProtocol.TCP),
            New TaowaEasyTierPortForward(New IPEndPoint(IPAddress.Any, LocalPort), Remote, TaowaEasyTierProtocol.UDP),
            New TaowaEasyTierPortForward(New IPEndPoint(IPAddress.IPv6Any, LocalPort), Remote, TaowaEasyTierProtocol.TCP),
            New TaowaEasyTierPortForward(New IPEndPoint(IPAddress.IPv6Any, LocalPort), Remote, TaowaEasyTierProtocol.UDP)
        }

        If EasyTier.AddPortForward(Forwards) Then Return LocalPort
        Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
        Return 0
    End Function

    Private Sub GuestProfileLoop(Session As TaowaScaffoldingClientSession, LocalProfile As TaowaProfile, EasyTier As TaowaEasyTierProcess, Capture As TaowaStateCapture)
        Do
            Thread.Sleep(5000)

            Dim PingBody = Encoding.UTF8.GetBytes(New JObject(
                New JProperty("machine_id", LocalProfile.MachineId),
                New JProperty("name", LocalProfile.Name),
                New JProperty("vendor", LocalProfile.Vendor)
            ).ToString(Newtonsoft.Json.Formatting.None))
            If Session.SendSync("c", "player_ping", PingBody) Is Nothing Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.PingHostFail, Capture)
                Return
            End If

            Dim Response = Session.SendSync("c", "player_profiles_list")
            If Response Is Nothing Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.PingHostFail, Capture)
                Return
            End If

            Dim Profiles = ParseServerProfiles(Response.Data, LocalProfile)
            If Profiles Is Nothing Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.ScaffoldingInvalidResponse, Capture)
                Return
            End If

            If Not EasyTier.IsAlive Then
                Pcl2TaowaCore.SetException(TaowaExceptionType.GuestEasytierCrash, Capture)
                Return
            End If

            If Not Pcl2TaowaCore.SetGuestProfiles(Profiles, Capture) Then Return
        Loop
    End Sub

    Private Function ParseServerProfiles(Data As Byte(), LocalProfile As TaowaProfile) As List(Of TaowaProfile)
        Try
            Dim Root = JArray.Parse(Encoding.UTF8.GetString(Data))
            Dim Profiles As New List(Of TaowaProfile)
            Dim HostFound = False
            Dim LocalFound = False

            For Each Item In Root
                Dim Obj = TryCast(Item, JObject)
                If Obj Is Nothing Then Return Nothing

                Dim Name = ReadString(Obj, "name")
                Dim MachineId = ReadString(Obj, "machine_id")
                Dim Vendor = ReadString(Obj, "vendor")
                Dim KindText = ReadString(Obj, "kind")
                If Name Is Nothing OrElse MachineId Is Nothing OrElse Vendor Is Nothing Then Return Nothing

                Dim Kind As TaowaProfileKind
                If String.Equals(MachineId, LocalProfile.MachineId, StringComparison.Ordinal) Then
                    If LocalFound Then Return Nothing
                    LocalFound = True
                    Kind = TaowaProfileKind.LOCAL
                ElseIf String.Equals(KindText, "HOST", StringComparison.Ordinal) AndAlso Not HostFound Then
                    HostFound = True
                    Kind = TaowaProfileKind.HOST
                ElseIf String.Equals(KindText, "GUEST", StringComparison.Ordinal) Then
                    Kind = TaowaProfileKind.GUEST
                Else
                    Return Nothing
                End If

                Profiles.Add(New TaowaProfile(MachineId, Name, Vendor, Kind))
            Next

            If Not HostFound Then Return Nothing
            If Not LocalFound Then Profiles.Add(LocalProfile.Clone())

            Profiles = Profiles.OrderBy(Function(Profile) Profile.MachineId, StringComparer.Ordinal).ToList()
            For i = 1 To Profiles.Count - 1
                If String.Equals(Profiles(i - 1).MachineId, Profiles(i).MachineId, StringComparison.Ordinal) Then Return Nothing
            Next
            Return Profiles
        Catch
            Return Nothing
        End Try
    End Function

    Private Function ReadString(Obj As JObject, Name As String) As String
        Dim Token = Obj.SelectToken(Name)
        If Token Is Nothing Then Return Nothing
        Return Token.ToString()
    End Function

    Private Function MergePublicNodes(PublicNodes As IEnumerable(Of String)) As List(Of String)
        Dim Result As New List(Of String)
        If PublicNodes IsNot Nothing Then
            For Each Node In PublicNodes
                If Not String.IsNullOrWhiteSpace(Node) Then Result.Add(Node.Trim())
            Next
        End If
        For Each Node In Pcl2TaowaEasyTier.DefaultPublicServers
            If Not Result.Contains(Node) Then Result.Add(Node)
        Next
        Return Result.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Sub Log(Message As String)
        Try
            Dim LogFolder = Path.Combine(If(AppDomain.CurrentDomain.BaseDirectory, "."), "PCL")
            DirectoryUtils.Create(LogFolder)
            File.AppendAllText(Path.Combine(LogFolder, "TaowaInternal.log"), Date.Now.ToString("HH:mm:ss.fff") & " " & Message & vbCrLf, Encoding.UTF8)
        Catch
        End Try
    End Sub

End Module

Public Class TaowaResolvedScaffolding
    Public ReadOnly Property LocalPort As Integer
    Public ReadOnly Property HostAddress As IPAddress

    Public Sub New(LocalPort As Integer, HostAddress As IPAddress)
        Me.LocalPort = LocalPort
        Me.HostAddress = HostAddress
    End Sub
End Class
