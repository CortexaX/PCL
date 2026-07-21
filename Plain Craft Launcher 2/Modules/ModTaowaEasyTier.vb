Imports System.Net.Sockets

Public Module Pcl2TaowaEasyTier

    Private Const TimeoutMilliseconds As Integer = 64000

    Public ReadOnly Property DefaultPublicServers As String()
        Get
            Return {
                "tcp://public.easytier.top:11010",
                "tcp://public2.easytier.cn:54321",
                "https://etnode.zkitefly.eu.org/node1",
                "https://etnode.zkitefly.eu.org/node2"
            }
        End Get
    End Property

    Public Function BuildScaffoldingHostArguments(Room As TaowaRoom, PublicServers As IEnumerable(Of String), ScaffoldingPort As Integer, MinecraftPort As Integer) As List(Of TaowaEasyTierArgument)
        Dim Result = BuildScaffoldingBaseArguments(Room, PublicServers)
        Result.Add(TaowaEasyTierArgument.HostName("scaffolding-mc-server-" & ScaffoldingPort))
        Result.Add(TaowaEasyTierArgument.IPv4(IPAddress.Parse("10.144.144.1")))
        Result.Add(TaowaEasyTierArgument.TcpWhitelist(ScaffoldingPort))
        Result.Add(TaowaEasyTierArgument.TcpWhitelist(MinecraftPort))
        Result.Add(TaowaEasyTierArgument.UdpWhitelist(MinecraftPort))
        Return Result
    End Function

    Public Function BuildScaffoldingGuestArguments(Room As TaowaRoom, PublicServers As IEnumerable(Of String)) As List(Of TaowaEasyTierArgument)
        Dim Result = BuildScaffoldingBaseArguments(Room, PublicServers)
        Result.Add(TaowaEasyTierArgument.DHCP())
        Result.Add(TaowaEasyTierArgument.TcpWhitelist(0))
        Result.Add(TaowaEasyTierArgument.UdpWhitelist(0))
        Return Result
    End Function

    Public Function BuildScaffoldingBaseArguments(Room As TaowaRoom, PublicServers As IEnumerable(Of String)) As List(Of TaowaEasyTierArgument)
        If Room Is Nothing Then Throw New ArgumentNullException(NameOf(Room))

        Dim Result As New List(Of TaowaEasyTierArgument)
        Result.Add(TaowaEasyTierArgument.NetworkName(Room.NetworkName))
        Result.Add(TaowaEasyTierArgument.NetworkSecret(Room.NetworkSecret))

        If PublicServers IsNot Nothing Then
            For Each Server In PublicServers
                If Not String.IsNullOrWhiteSpace(Server) Then Result.Add(TaowaEasyTierArgument.PublicServer(Server.Trim()))
            Next
        End If

        Result.AddRange({
            TaowaEasyTierArgument.NoTun(),
            TaowaEasyTierArgument.Compression("zstd"),
            TaowaEasyTierArgument.MultiThread(),
            TaowaEasyTierArgument.LatencyFirst(),
            TaowaEasyTierArgument.EnableKcpProxy(),
            TaowaEasyTierArgument.Listener(New IPEndPoint(IPAddress.Any, 0), TaowaEasyTierProtocol.UDP),
            TaowaEasyTierArgument.Listener(New IPEndPoint(IPAddress.Any, 0), TaowaEasyTierProtocol.TCP),
            TaowaEasyTierArgument.P2POnly()
        })
        Return Result
    End Function

    Public Function BuildCommandArguments(Arguments As IEnumerable(Of TaowaEasyTierArgument)) As List(Of String)
        Dim Result As New List(Of String)
        If Arguments Is Nothing Then Return Result

        For Each Argument In Arguments
            Select Case Argument.Kind
                Case TaowaEasyTierArgumentKind.NoTun
                    Result.Add("--no-tun")
                Case TaowaEasyTierArgumentKind.Compression
                    Result.Add("--compression=" & Argument.Value)
                Case TaowaEasyTierArgumentKind.MultiThread
                    Result.Add("--multi-thread")
                Case TaowaEasyTierArgumentKind.LatencyFirst
                    Result.Add("--latency-first")
                Case TaowaEasyTierArgumentKind.EnableKcpProxy
                    Result.Add("--enable-kcp-proxy")
                Case TaowaEasyTierArgumentKind.NetworkName
                    Result.Add("--network-name")
                    Result.Add(Argument.Value)
                Case TaowaEasyTierArgumentKind.NetworkSecret
                    Result.Add("--network-secret")
                    Result.Add(Argument.Value)
                Case TaowaEasyTierArgumentKind.PublicServer
                    Result.Add("-p")
                    Result.Add(Argument.Value)
                Case TaowaEasyTierArgumentKind.Listener
                    Result.Add("-l")
                    Result.Add(ProtocolName(Argument.Proto) & "://" & FormatEndPoint(Argument.Address))
                Case TaowaEasyTierArgumentKind.PortForward
                    Result.Add("--port-forward=" & ProtocolName(Argument.Forward.Proto) & "://" &
                               FormatEndPoint(Argument.Forward.Local) & "/" & FormatEndPoint(Argument.Forward.Remote))
                Case TaowaEasyTierArgumentKind.DHCP
                    Result.Add("-d")
                Case TaowaEasyTierArgumentKind.HostName
                    Result.Add("--hostname")
                    Result.Add(Argument.Value)
                Case TaowaEasyTierArgumentKind.IPv4
                    Result.Add("--ipv4")
                    Result.Add(Argument.Address.Address.ToString())
                Case TaowaEasyTierArgumentKind.TcpWhitelist
                    Result.Add("--tcp-whitelist=" & Argument.Port)
                Case TaowaEasyTierArgumentKind.UdpWhitelist
                    Result.Add("--udp-whitelist=" & Argument.Port)
                Case TaowaEasyTierArgumentKind.P2POnly
                    Result.Add("--p2p-only")
            End Select
        Next
        Return Result
    End Function

    Public Function CalculateConnectionDifficulty(Left As TaowaNatType, Right As TaowaNatType) As TaowaConnectionDifficulty
        If NatContains(Left, Right, TaowaNatType.OpenInternet) Then
            Return TaowaConnectionDifficulty.Easiest
        ElseIf NatContains(Left, Right, TaowaNatType.NoPAT, TaowaNatType.FullCone) Then
            Return TaowaConnectionDifficulty.Simple
        ElseIf NatContains(Left, Right, TaowaNatType.Restricted, TaowaNatType.PortRestricted) Then
            Return TaowaConnectionDifficulty.Medium
        Else
            Return TaowaConnectionDifficulty.Tough
        End If
    End Function

    Private Function NatContains(Left As TaowaNatType, Right As TaowaNatType, ParamArray Values As TaowaNatType()) As Boolean
        For Each Value In Values
            If Left = Value OrElse Right = Value Then Return True
        Next
        Return False
    End Function

    Public Function RequestSpecificPort(Port As Integer) As Integer?
        Dim Listener As TcpListener = Nothing
        Try
            Listener = New TcpListener(IPAddress.Loopback, Port)
            Listener.Start()
            Return DirectCast(Listener.LocalEndpoint, IPEndPoint).Port
        Catch
            Return Nothing
        Finally
            Try
                If Listener IsNot Nothing Then Listener.Stop()
            Catch
            End Try
        End Try
    End Function

    Public Function RequestPort(Kind As TaowaPortRequest) As Integer
        Dim Listener As TcpListener = Nothing
        Try
            Listener = New TcpListener(IPAddress.Loopback, 0)
            Listener.Start()
            Return DirectCast(Listener.LocalEndpoint, IPEndPoint).Port
        Catch
            Return 35780 + CInt(Kind)
        Finally
            Try
                If Listener IsNot Nothing Then Listener.Stop()
            Catch
            End Try
        End Try
    End Function

    Public Function ResolveTools() As TaowaEasyTierTools
        Dim BaseDir = If(AppDomain.CurrentDomain.BaseDirectory, ".")
        Dim CandidateFolders = {
            Path.Combine(BaseDir, "Resources", "Taowa", "EasyTier"),
            Path.Combine(BaseDir, "Resources", "Taowa"),
            Path.Combine(PathTemp, "Taowa", "embedded-easytier")
        }

        For Each Folder In CandidateFolders
            Dim Entry = Path.Combine(Folder, "easytier-core.exe")
            Dim Cli = Path.Combine(Folder, "easytier-cli.exe")
            If File.Exists(Entry) AndAlso File.Exists(Cli) Then Return New TaowaEasyTierTools(Entry, Cli)
        Next

        Throw New FileNotFoundException("easytier-core.exe/easytier-cli.exe not found")
    End Function

    Friend Function CreateStartInfo(FileName As String, Arguments As IEnumerable(Of String)) As ProcessStartInfo
        Dim Info As New ProcessStartInfo With {
            .FileName = FileName,
            .Arguments = QuoteArguments(Arguments),
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .WorkingDirectory = Path.GetTempPath()
        }
        StripProxyFromProcess(Info)
        Return Info
    End Function

    Friend Function ProtocolName(Proto As TaowaEasyTierProtocol) As String
        Select Case Proto
            Case TaowaEasyTierProtocol.TCP
                Return "tcp"
            Case TaowaEasyTierProtocol.UDP
                Return "udp"
            Case Else
                Return "tcp"
        End Select
    End Function

    Friend Function FormatEndPoint(Address As IPEndPoint) As String
        If Address Is Nothing Then Return ""
        Return Address.ToString()
    End Function

    Friend Function ParseNatType(Value As String) As TaowaNatType?
        Select Case Value
            Case "Unknown"
                Return TaowaNatType.Unknown
            Case "OpenInternet"
                Return TaowaNatType.OpenInternet
            Case "NoPat"
                Return TaowaNatType.NoPAT
            Case "FullCone"
                Return TaowaNatType.FullCone
            Case "Restricted"
                Return TaowaNatType.Restricted
            Case "PortRestricted"
                Return TaowaNatType.PortRestricted
            Case "Symmetric"
                Return TaowaNatType.Symmetric
            Case "SymUdpFirewall"
                Return TaowaNatType.SymmetricUdpWall
            Case "SymmetricEasyInc"
                Return TaowaNatType.SymmetricEasyIncrease
            Case "SymmetricEasyDec"
                Return TaowaNatType.SymmetricEasyDecrease
            Case Else
                Return Nothing
        End Select
    End Function

    Private Function QuoteArguments(Arguments As IEnumerable(Of String)) As String
        If Arguments Is Nothing Then Return ""
        Return String.Join(" ", Arguments.Select(Function(Value) QuoteArgument(If(Value, ""))))
    End Function

    Private Function QuoteArgument(Value As String) As String
        If Value.Length = 0 Then Return """"""
        If Value.IndexOfAny(New Char() {" "c, ControlChars.Tab, """"c}) < 0 Then Return Value
        Return """" & Value.Replace("""", "\" & """") & """"
    End Function

    Private Sub StripProxyFromProcess(Info As ProcessStartInfo)
        Try
            For Each Key In {
                "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
                "http_proxy", "https_proxy", "all_proxy", "no_proxy",
                "FTP_PROXY", "ftp_proxy", "SOCKS_PROXY", "socks_proxy"
            }
                Try
                    If Info.EnvironmentVariables.ContainsKey(Key) Then Info.EnvironmentVariables.Remove(Key)
                Catch
                End Try
            Next
            Info.EnvironmentVariables("NO_PROXY") = "*"
            Info.EnvironmentVariables("no_proxy") = "*"
            Info.EnvironmentVariables("HTTP_PROXY") = ""
            Info.EnvironmentVariables("HTTPS_PROXY") = ""
            Info.EnvironmentVariables("ALL_PROXY") = ""
            Info.EnvironmentVariables("http_proxy") = ""
            Info.EnvironmentVariables("https_proxy") = ""
            Info.EnvironmentVariables("all_proxy") = ""
        Catch ex As Exception
            Log("StripProxyFromProcess: " & ex.Message)
        End Try
    End Sub

    Friend Sub Log(Message As String)
        Try
            Dim LogFolder = Path.Combine(If(AppDomain.CurrentDomain.BaseDirectory, "."), "PCL")
            DirectoryUtils.Create(LogFolder)
            File.AppendAllText(Path.Combine(LogFolder, "TaowaEasyTier.log"), Date.Now.ToString("HH:mm:ss.fff") & " " & Message & vbCrLf, Encoding.UTF8)
        Catch
        End Try
    End Sub

End Module

Public Enum TaowaPortRequest
    EasyTierRPC = 0
    Scaffolding = 1
    Minecraft = 2
End Enum

Public Enum TaowaEasyTierProtocol
    TCP
    UDP
End Enum

Public Enum TaowaNatType
    Unknown
    OpenInternet
    NoPAT
    FullCone
    Restricted
    PortRestricted
    Symmetric
    SymmetricUdpWall
    SymmetricEasyIncrease
    SymmetricEasyDecrease
End Enum

Public Enum TaowaEasyTierArgumentKind
    NoTun
    Compression
    MultiThread
    LatencyFirst
    EnableKcpProxy
    NetworkName
    NetworkSecret
    PublicServer
    Listener
    PortForward
    DHCP
    HostName
    IPv4
    TcpWhitelist
    UdpWhitelist
    P2POnly
End Enum

Public Class TaowaEasyTierTools
    Public ReadOnly Property Entry As String
    Public ReadOnly Property Cli As String

    Public Sub New(Entry As String, Cli As String)
        Me.Entry = Entry
        Me.Cli = Cli
    End Sub
End Class

Public Class TaowaEasyTierPortForward
    Public ReadOnly Property Local As IPEndPoint
    Public ReadOnly Property Remote As IPEndPoint
    Public ReadOnly Property Proto As TaowaEasyTierProtocol

    Public Sub New(Local As IPEndPoint, Remote As IPEndPoint, Proto As TaowaEasyTierProtocol)
        Me.Local = Local
        Me.Remote = Remote
        Me.Proto = Proto
    End Sub
End Class

Public Class TaowaEasyTierArgument
    Public ReadOnly Property Kind As TaowaEasyTierArgumentKind
    Public ReadOnly Property Value As String
    Public ReadOnly Property Address As IPEndPoint
    Public ReadOnly Property Forward As TaowaEasyTierPortForward
    Public ReadOnly Property Proto As TaowaEasyTierProtocol
    Public ReadOnly Property Port As Integer

    Private Sub New(Kind As TaowaEasyTierArgumentKind, Optional Value As String = Nothing, Optional Address As IPEndPoint = Nothing,
                    Optional Proto As TaowaEasyTierProtocol = TaowaEasyTierProtocol.TCP, Optional Port As Integer = 0,
                    Optional Forward As TaowaEasyTierPortForward = Nothing)
        Me.Kind = Kind
        Me.Value = Value
        Me.Address = Address
        Me.Proto = Proto
        Me.Port = Port
        Me.Forward = Forward
    End Sub

    Public Shared Function NoTun() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.NoTun)
    End Function

    Public Shared Function Compression(Method As String) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.Compression, Value:=Method)
    End Function

    Public Shared Function MultiThread() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.MultiThread)
    End Function

    Public Shared Function LatencyFirst() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.LatencyFirst)
    End Function

    Public Shared Function EnableKcpProxy() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.EnableKcpProxy)
    End Function

    Public Shared Function NetworkName(Name As String) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.NetworkName, Value:=Name)
    End Function

    Public Shared Function NetworkSecret(Secret As String) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.NetworkSecret, Value:=Secret)
    End Function

    Public Shared Function PublicServer(Server As String) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.PublicServer, Value:=Server)
    End Function

    Public Shared Function Listener(Address As IPEndPoint, Proto As TaowaEasyTierProtocol) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.Listener, Address:=Address, Proto:=Proto)
    End Function

    Public Shared Function PortForward(Forward As TaowaEasyTierPortForward) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.PortForward, Forward:=Forward)
    End Function

    Public Shared Function DHCP() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.DHCP)
    End Function

    Public Shared Function HostName(Name As String) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.HostName, Value:=Name)
    End Function

    Public Shared Function IPv4(Address As IPAddress) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.IPv4, Address:=New IPEndPoint(Address, 0))
    End Function

    Public Shared Function TcpWhitelist(Port As Integer) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.TcpWhitelist, Port:=Port)
    End Function

    Public Shared Function UdpWhitelist(Port As Integer) As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.UdpWhitelist, Port:=Port)
    End Function

    Public Shared Function P2POnly() As TaowaEasyTierArgument
        Return New TaowaEasyTierArgument(TaowaEasyTierArgumentKind.P2POnly)
    End Function
End Class

Public Class TaowaEasyTierMember
    Public ReadOnly Property Hostname As String
    Public ReadOnly Property Address As IPAddress
    Public ReadOnly Property IsLocal As Boolean
    Public ReadOnly Property Nat As TaowaNatType

    Public Sub New(Hostname As String, Address As IPAddress, IsLocal As Boolean, Nat As TaowaNatType)
        Me.Hostname = Hostname
        Me.Address = Address
        Me.IsLocal = IsLocal
        Me.Nat = Nat
    End Sub
End Class

Public Class TaowaEasyTierProcess
    Implements IDisposable

    Private Const TimeoutMilliseconds As Integer = 64000
    Private ReadOnly Tools As TaowaEasyTierTools
    Private ReadOnly RpcPort As Integer
    Private ReadOnly CoreProcess As Process

    Private Sub New(Tools As TaowaEasyTierTools, RpcPort As Integer, CoreProcess As Process)
        Me.Tools = Tools
        Me.RpcPort = RpcPort
        Me.CoreProcess = CoreProcess
    End Sub

    Public Shared Function Create(Arguments As IEnumerable(Of TaowaEasyTierArgument)) As TaowaEasyTierProcess
        Dim Tools = Pcl2TaowaEasyTier.ResolveTools()
        Dim Rpc = Pcl2TaowaEasyTier.RequestPort(TaowaPortRequest.EasyTierRPC)
        Dim CommandArguments = Pcl2TaowaEasyTier.BuildCommandArguments(Arguments)
        CommandArguments.Add("-r")
        CommandArguments.Add(Rpc.ToString())

        Pcl2TaowaEasyTier.Log("Starting easytier: " & String.Join(" ", CommandArguments) & ", rpc=" & Rpc)

        Dim Core As New Process With {
            .StartInfo = Pcl2TaowaEasyTier.CreateStartInfo(Tools.Entry, CommandArguments),
            .EnableRaisingEvents = True
        }
        AddHandler Core.OutputDataReceived, Sub(__, e)
                                                If e.Data IsNot Nothing Then Pcl2TaowaEasyTier.Log("[core] " & e.Data)
                                            End Sub
        AddHandler Core.ErrorDataReceived, Sub(__, e)
                                               If e.Data IsNot Nothing Then Pcl2TaowaEasyTier.Log("[core] " & e.Data)
                                           End Sub

        If Not Core.Start() Then Throw New InvalidOperationException("Cannot start easytier-core.exe")
        Core.BeginOutputReadLine()
        Core.BeginErrorReadLine()

        Return New TaowaEasyTierProcess(Tools, Rpc, Core)
    End Function

    Public ReadOnly Property IsAlive As Boolean
        Get
            Try
                Return CoreProcess IsNot Nothing AndAlso Not CoreProcess.HasExited
            Catch
                Return False
            End Try
        End Get
    End Property

    Public Function GetPlayers() As List(Of TaowaEasyTierMember)
        Dim Output = RunCli({"-p", "127.0.0.1:" & RpcPort, "-o", "json", "peer"})
        If Output Is Nothing Then Return Nothing

        Try
            Dim Root = JArray.Parse(Output)
            Dim Result As New List(Of TaowaEasyTierMember)
            For Each Item In Root
                Dim Obj = TryCast(Item, JObject)
                If Obj Is Nothing Then Return Nothing

                Dim Hostname = ReadString(Obj, "hostname")
                Dim AddressText = ReadString(Obj, "ipv4")
                Dim Cost = ReadString(Obj, "cost")
                Dim NatText = ReadString(Obj, "nat_type")
                If Hostname Is Nothing OrElse Cost Is Nothing OrElse NatText Is Nothing Then Return Nothing

                Dim Address As IPAddress = Nothing
                If Not String.IsNullOrEmpty(AddressText) Then IPAddress.TryParse(AddressText, Address)

                Dim Nat = Pcl2TaowaEasyTier.ParseNatType(NatText)
                If Not Nat.HasValue Then Return Nothing

                Result.Add(New TaowaEasyTierMember(Hostname, Address, String.Equals(Cost, "Local", StringComparison.Ordinal), Nat.Value))
            Next
            Return Result
        Catch
            Return Nothing
        End Try
    End Function

    Public Function AddPortForward(Forwards As IEnumerable(Of TaowaEasyTierPortForward)) As Boolean
        Dim Pending = If(Forwards, New List(Of TaowaEasyTierPortForward)).ToList()
        If Pending.Count = 0 Then Return True

        For Attempt = 0 To 2
            For i = Pending.Count - 1 To 0 Step -1
                Dim Forward = Pending(i)
                Dim Args = {
                    "-p", "127.0.0.1:" & RpcPort,
                    "port-forward", "add",
                    Pcl2TaowaEasyTier.ProtocolName(Forward.Proto),
                    Pcl2TaowaEasyTier.FormatEndPoint(Forward.Local),
                    Pcl2TaowaEasyTier.FormatEndPoint(Forward.Remote)
                }
                If RunCli(Args) IsNot Nothing Then Pending.RemoveAt(i)
            Next

            If Pending.Count = 0 Then Return True
            Thread.Sleep(Attempt * 1000 + 500)
        Next

        Pcl2TaowaEasyTier.Log("Cannot add port-forward rules: " &
                              String.Join(", ", Pending.Select(Function(Forward) Pcl2TaowaEasyTier.FormatEndPoint(Forward.Local) &
                                                                         " -> " & Pcl2TaowaEasyTier.FormatEndPoint(Forward.Remote) &
                                                                         " (" & Pcl2TaowaEasyTier.ProtocolName(Forward.Proto) & ")")))
        Return False
    End Function

    Private Function RunCli(Arguments As IEnumerable(Of String)) As String
        Try
            Using Cli As New Process With {.StartInfo = Pcl2TaowaEasyTier.CreateStartInfo(Tools.Cli, Arguments)}
                If Not Cli.Start() Then Return Nothing
                Dim StdOutTask = Cli.StandardOutput.ReadToEndAsync()
                Dim StdErrTask = Cli.StandardError.ReadToEndAsync()
                If Not Cli.WaitForExit(TimeoutMilliseconds) Then
                    Try
                        Cli.Kill()
                    Catch
                    End Try
                    Return Nothing
                End If

                Dim StdErr = StdErrTask.Result
                If Not String.IsNullOrWhiteSpace(StdErr) Then Pcl2TaowaEasyTier.Log("[cli] " & StdErr.Trim())
                If Cli.ExitCode <> 0 Then Return Nothing
                Return StdOutTask.Result
            End Using
        Catch ex As Exception
            Pcl2TaowaEasyTier.Log("EasyTier CLI failed: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ReadString(Obj As JObject, Name As String) As String
        Dim Token = Obj.SelectToken(Name)
        If Token Is Nothing Then Return Nothing
        Return Token.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If CoreProcess IsNot Nothing AndAlso Not CoreProcess.HasExited Then CoreProcess.Kill()
        Catch
        End Try
    End Sub
End Class
