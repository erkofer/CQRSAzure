﻿Imports Microsoft.WindowsAzure.Storage.Table

Namespace Azure.Table
    Public Class TableEventStreamWriter(Of TAggregate As CQRSAzure.EventSourcing.IAggregationIdentifier, TAggregationKey)
        Inherits TableEventStreamBase(Of TAggregate, TAggregationKey)
        Implements IEventStreamWriter(Of TAggregate, TAggregationKey)

#Region "Event stream information"
        Public ReadOnly Property Key As TAggregationKey Implements IEventStream(Of TAggregate, TAggregationKey).Key
            Get
                Throw New NotImplementedException()
            End Get
        End Property

        Public ReadOnly Property RecordCount As ULong Implements IEventStream(Of TAggregate, TAggregationKey).RecordCount
            Get
                Throw New NotImplementedException()
            End Get
        End Property

        Public ReadOnly Property LastAddition As Date? Implements IEventStream(Of TAggregate, TAggregationKey).LastAddition
            Get
                Throw New NotImplementedException()
            End Get
        End Property
#End Region

        Public Sub AppendEvent(EventInstance As IEvent(Of TAggregate)) Implements IEventStreamWriter(Of TAggregate, TAggregationKey).AppendEvent
            Throw New NotImplementedException()
        End Sub

        Public Sub AppendEvents(StartingVersion As UInteger, Events As IEnumerable(Of IEvent(Of TAggregate))) Implements IEventStreamWriter(Of TAggregate, TAggregationKey).AppendEvents
            Throw New NotImplementedException()
        End Sub

        Private Sub New(ByVal AggregateDomainName As String, ByVal AggregateKey As TAggregationKey)
            MyBase.New(AggregateDomainName, AggregateKey)

        End Sub

    End Class
End Namespace