﻿Imports Microsoft.WindowsAzure.Storage.Blob
Imports Microsoft.WindowsAzure.Storage
Imports Microsoft.WindowsAzure.Storage.Auth
Imports Microsoft.WindowsAzure
Imports Microsoft.Azure
Imports System.Configuration
Imports CQRSAzure.EventSourcing
Imports CQRSAzure.EventSourcing.Azure.Blob

Namespace Azure.Blob
    ''' <summary>
    ''' Common functionality that both reader and writer use to access the event store on the Azure storage
    ''' </summary>
    Public MustInherit Class BlobEventStreamBase(Of TAggregate As CQRSAzure.EventSourcing.IAggregationIdentifier, TAggregateKey)
        Inherits BlobEventStreamBase


        Private ReadOnly m_blob As CloudAppendBlob
        Protected ReadOnly m_key As TAggregateKey
        Private ReadOnly m_converter As IKeyConverter(Of TAggregateKey)



        Protected ReadOnly Property AggregateClassName As String
            Get
                Dim typeName As String = GetType(TAggregate).Name
                Return MakeValidStorageFolderName(typeName)
            End Get
        End Property

        ''' <summary>
        ''' The storage location that the event streams for this event stream are located
        ''' </summary>
        ''' <returns>
        ''' /eventstreams/[aggregate-class-name]/
        ''' </returns>
        ''' <remarks>
        ''' This is a virtual path that is added to the blob name to create a traversable path
        ''' </remarks>
        Protected ReadOnly Property ContainerBasePath As String
            Get
                Return EVENTSTREAM_FOLDER & "/" & MakeValidStorageFolderName(AggregateClassName) & "/"
            End Get
        End Property

        ''' <summary>
        ''' The filename that this specific event stream will be written to
        ''' </summary>
        ''' <returns>
        ''' e.g. /eventstreams/car/olj565m.events 
        ''' </returns>
        Protected ReadOnly Property EventStreamBlobFilename As String
            Get
                Return ContainerBasePath & MakeValidStorageFolderName(m_key.ToString) & ".events"
            End Get
        End Property

        ''' <summary>
        ''' The blob that the events are written to/read from
        ''' </summary>
        Protected ReadOnly Property AppendBlob As CloudAppendBlob
            Get
                Return m_blob
            End Get
        End Property

        ''' <summary>
        ''' The system-wide options for writing to the event stream
        ''' </summary>
        ''' <remarks>
        ''' 
        ''' </remarks>
        Private ReadOnly Property RequestOptions As BlobRequestOptions
            Get
                Dim ret As New BlobRequestOptions()

                'As the event stream MUST be single-writer we can ignore conditional errors when retrying
                ret.AbsorbConditionalErrorsOnRetry = True

                ret.RetryPolicy = New RetryPolicies.LinearRetry()

                'If required, encryption keys could be on a per-key basis...

                Return ret
            End Get
        End Property

        Public Function GetSequence() As Long

            If (AppendBlob IsNot Nothing) Then
                Try
                    AppendBlob.FetchAttributes()
                    Dim m_sequence As Long
                    If (Long.TryParse(AppendBlob.Metadata(METADATA_SEQUENCE), m_sequence)) Then
                        Return m_sequence
                    End If

                Catch exBlob As Microsoft.WindowsAzure.Storage.StorageException
                    Throw New EventStreamReadException(DomainName, AggregateClassName, m_key.ToString(), 0, "Unable to get the sequence number for this event stream", exBlob)
                End Try
            Else
                Throw New EventStreamReadException(DomainName, AggregateClassName, m_key.ToString(), 0, "Unable to get the sequence number for this event stream")
            End If

            Return 0

        End Function

        Public Function GetRecordCount() As Long

            If (AppendBlob IsNot Nothing) Then
                Try
                    AppendBlob.FetchAttributes()
                    Dim m_sequence As Long
                    If (Long.TryParse(AppendBlob.Metadata(METADATA_RECORD_COUNT), m_sequence)) Then
                        Return m_sequence
                    End If

                Catch exBlob As Microsoft.WindowsAzure.Storage.StorageException
                    Throw New EventStreamReadException(DomainName, AggregateClassName, m_key.ToString(), 0, "Unable to get the record countr for this event stream", exBlob)
                End Try
            Else
                Throw New EventStreamReadException(DomainName, AggregateClassName, m_key.ToString(), 0, "Unable to get the record countr for this event stream")
            End If

            Return 0

        End Function

        ''' <summary>
        ''' Create a new base for a reader or writer class in the given domain
        ''' </summary>
        ''' <param name="AggregateDomainName">
        ''' The name of the domain to store/retrieve the event streams under
        ''' </param>
        Protected Sub New(ByVal AggregateDomainName As String,
                          ByVal AggregateKey As TAggregateKey,
                          Optional ByVal writeAccess As Boolean = False,
                          Optional ByVal connectionStringName As String = "",
                          Optional ByVal settings As IBlobStreamSettings = Nothing)
            MyBase.New(AggregateDomainName, writeAccess, connectionStringName, settings)

            'Get the aggregation instance key to use when creating a blob file name
            m_key = AggregateKey

            If (BlobContainer IsNot Nothing) Then
                m_blob = BlobContainer.GetAppendBlobReference(EventStreamBlobFilename)
            End If
            Call ResetBlob()

            m_converter = KeyConverterFactory.CreateKeyConverter(Of TAggregateKey)

        End Sub

        Protected Sub ResetBlob()

            If (BlobContainer IsNot Nothing) Then
                If Not m_blob.Exists() Then
                    'Make the file to append to if it doesn't already exist
                    m_blob.CreateOrReplace()
                    'Set the initial metadata
                    m_blob.Metadata(METATDATA_DOMAIN) = DomainName
                    m_blob.Metadata(METADATA_AGGREGATE_CLASS) = GetType(TAggregate).Name
                    m_blob.Metadata(METADATA_AGGREGATE_KEY) = m_key.ToString()
                    m_blob.Metadata(METADATA_SEQUENCE) = "0" 'Sequence starts at zero
                    m_blob.Metadata(METADATA_RECORD_COUNT) = "0" 'Record count starts at zero
                    m_blob.Metadata(METADATA_DATE_CREATED) = DateTime.UtcNow.ToString("O") 'use universal date/time
                    m_blob.SetMetadata()
                Else
                    m_blob.FetchAttributes()
                End If
            End If

        End Sub

        Protected Function GetAllStreamKeysBase(asOfDate As Date?) As IEnumerable(Of TAggregateKey)

            Dim ret As New List(Of TAggregateKey)

            If MyBase.BlobContainer IsNot Nothing Then
                Dim bd As CloudBlobDirectory = MyBase.BlobContainer.GetDirectoryReference(ContainerBasePath)
                For Each thisBlob In bd.ListBlobs(blobListingDetails:=BlobListingDetails.Metadata)

                    Dim blobFile As CloudBlob = TryCast(thisBlob, CloudBlob)
                    Dim ignore As Boolean = False
                    If (blobFile IsNot Nothing) Then
                        'Get the metadata for this blob
                        If (asOfDate.HasValue) Then
                            'if the creation date is after as-of-date then skip it
                            Dim creationDate As DateTime
                            If (blobFile.Metadata.ContainsKey(METADATA_DATE_CREATED)) Then
                                If DateTime.TryParseExact(blobFile.Metadata(METADATA_DATE_CREATED), "O", Nothing, Globalization.DateTimeStyles.None, creationDate) Then
                                    If (creationDate.Year > 1900) Then
                                        If (creationDate < asOfDate.Value) Then
                                            ignore = True
                                        End If
                                    End If
                                End If
                            End If
                        End If
                        If Not ignore Then
                            'add the key to the returned set
                            Dim keyString As String = blobFile.Metadata(METADATA_AGGREGATE_KEY)
                            If Not String.IsNullOrWhiteSpace(keyString) Then
                                'Try and turn it to the TAggregateKey
                                If (GetType(TAggregateKey) Is GetType(String)) Then
                                    ret.Add(CTypeDynamic(Of TAggregateKey)(keyString))
                                Else
                                    'need to convert the key as we had to store it as a string
                                    ret.Add(m_converter.FromString(keyString))
                                End If
                            End If
                        End If
                    End If
                Next
            End If

            Return ret

        End Function
    End Class


    ''' <summary>
    ''' Common functionality that both reader and writer use to access the event store on the Azure storage
    ''' </summary>
    Public MustInherit Class BlobEventStreamBase
        Inherits AzureStorageEventStreamBase

#Region "private members"
        Private ReadOnly m_blobClient As CloudBlobClient
        Private ReadOnly m_blobBasePath As CloudBlobContainer
#End Region


        Public Const METATDATA_DOMAIN As String = "DOMAIN"
        Public Const METADATA_AGGREGATE_CLASS As String = "AGGREGATECLASS"
        Public Const METADATA_SEQUENCE As String = "SEQUENCE"
        Public Const METADATA_RECORD_COUNT As String = "RECORDCOUNT"
        Public Const METADATA_AGGREGATE_KEY As String = "AGGREGATEKEY"
        Public Const METADATA_DATE_CREATED As String = "DATECREATED"


        Public Const ORPHANS_FOLDER As String = "uncategorised"
        Public Const EVENTSTREAM_FOLDER As String = "eventstreams"
        Public Const SNAPSHOTS_FOLDER As String = "snapshots"



        Protected ReadOnly Property BlobContainer As CloudBlobContainer
            Get
                Return m_blobBasePath
            End Get
        End Property

        ''' <summary>
        ''' Turn a name into a valid folder name for azure blob storage
        ''' </summary>
        ''' <param name="rawName">
        ''' The name of the thing we want to turn into a blob storage folder name
        ''' </param>
        ''' <returns>
        ''' A folder name that can be used to locate this object type's event streams
        ''' </returns>
        ''' <remarks>
        ''' Container names must start With a letter Or number, And can contain only letters, numbers, And the dash (-) character.
        ''' Every dash (-) character must be immediately preceded And followed by a letter Or number; consecutive dashes are Not permitted in container names.
        ''' All letters in a container name must be lowercase.
        ''' Container names must be from 3 through 63 characters long.
        ''' </remarks>
        Public Shared Function MakeValidStorageFolderName(ByVal rawName As String) As String

            If (String.IsNullOrWhiteSpace(rawName)) Then
                ' Don't allow empty folder names - assign an orphan folder for it
                Return ORPHANS_FOLDER
            Else
                Dim invalidCharacters As Char() = " _!,.;':@£$%^&*()+=/\#~{}[]?<>"
                Dim cleanName As String = String.Join("-", rawName.Split(invalidCharacters))
                If (cleanName.StartsWith("-")) Then
                    cleanName = cleanName.TrimStart("-")
                End If
                If (cleanName.EndsWith("-")) Then
                    cleanName = cleanName.TrimEnd("-")
                End If
                If (cleanName.Length < 3) Then
                    cleanName += "-abc"
                End If
                If (cleanName.Length > 63) Then
                    Dim uniqueness As Integer = cleanName.GetHashCode()
                    Dim uniqueid As String = uniqueness.ToString()
                    cleanName = cleanName.Substring(0, 63 - uniqueid.Length) & uniqueid
                End If
                cleanName = cleanName.Replace("--", "-")
                Return cleanName.ToLower()

            End If

        End Function

        ''' <summary>
        ''' Create a new base for a reader or writer class in the given domain
        ''' </summary>
        ''' <param name="AggregateDomainName">
        ''' The name of the domain to store/retrieve the event streams under
        ''' </param>
        ''' <param name="connectionStringName">
        ''' The configuration name to use to look up the Azure storage account connection string - this 
        ''' allows the read and write side to have different connection strings
        ''' </param>
        ''' <param name="writeAccess">
        ''' Does the process need write access to the underlying storage account
        ''' </param>
        Protected Sub New(ByVal AggregateDomainName As String,
                          Optional ByVal writeAccess As Boolean = False,
                          Optional ByVal connectionStringName As String = "",
                          Optional ByVal settings As IBlobStreamSettings = Nothing)
            MyBase.New(AggregateDomainName, writeAccess:=writeAccess, connectionStringName:=connectionStringName, settings:=settings)

            If (m_storageAccount IsNot Nothing) Then
                m_blobClient = m_storageAccount.CreateCloudBlobClient()
                If (m_blobClient IsNot Nothing) Then
                    'Create the reference to this aggretate type's event stream base folder
                    'e.g. /[domain]/
                    m_blobBasePath = m_blobClient.GetContainerReference(MakeValidStorageFolderName(DomainName))
                    If (m_blobBasePath IsNot Nothing) Then
                        m_blobBasePath.CreateIfNotExists()
                    End If
                End If
            End If
        End Sub



    End Class

End Namespace