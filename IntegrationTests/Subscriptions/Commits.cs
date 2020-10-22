﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Speckle.Core.Api;
using Speckle.Core.Api.SubscriptionModels;
using Speckle.Core.Credentials;
using Speckle.Core.Models;
using Speckle.Core.Transports;
using Tests;

namespace TestsIntegration.Subscriptions
{
  public class Committs
  {
    public Client client;
    public ServerInfo testServer;
    public Account testUserAccount;

    private CommitInfo CommitCreatedInfo;
    private CommitInfo CommitUpdatedInfo;
    private CommitInfo CommitDeletedInfo;
    private ServerTransport myServerTransport;
    string commitId;
    string streamId;

    [OneTimeSetUp]
    public void Setup()
    {
      testServer = new ServerInfo { url = "http://127.0.0.1:3000", name = "TestServer" };
      testUserAccount = Fixtures.SeedUser(testServer);
      client = new Client(testUserAccount);
      myServerTransport = new ServerTransport(testUserAccount, null);
    }

    [Test, Order(0)]
    public async Task SubscribeCommitCreated()
    {
      var streamInput = new StreamCreateInput
      {
        description = "Hello World",
        name = "Super Stream 01"
      };

      streamId = await client.StreamCreate(streamInput);
      Assert.NotNull(streamId);

      var branchInput = new BranchCreateInput
      {
        description = "Just testing branch create...",
        name = "awesome-features",
        streamId = streamId
      };

      var branchId = await client.BranchCreate(branchInput);
      Assert.NotNull(branchId);

      client.SubscribeCommitCreated(streamId);
      client.OnCommitCreated += Client_OnCommitCreated;

      Thread.Sleep(100); //let server catch-up

      var myObject = new Base();
      var ptsList = new List<Point>();
      for (int i = 0; i < 100; i++)
        ptsList.Add(new Point(i, i, i));

      myObject["Points"] = ptsList;

      var objectId = await Operations.Send(myObject, new List<ITransport>() { myServerTransport }, false);

      var commitInput = new CommitCreateInput
      {
        streamId = streamId,
        branchName = "awesome-features",
        objectId = objectId,
        message = "sending some test points"
      };

      commitId  = await client.CommitCreate(commitInput);
      Assert.NotNull(commitId);

      await Task.Run(() => {
        Thread.Sleep(100); //let client catch-up
        Assert.NotNull(CommitCreatedInfo);
        Assert.AreEqual(commitInput.message, CommitCreatedInfo.message);
      });
    }

    private void Client_OnCommitCreated(object sender, CommitInfo e)
    {
      CommitCreatedInfo = e;
    }

    [Test, Order(1)]
    public async Task SubscribeCommitUpdated()
    {
      client.SubscribeCommitUpdated(streamId);
      client.OnCommitUpdated += Client_OnCommitUpdated;

      Thread.Sleep(100); //let server catch-up

      var commitInput = new CommitUpdateInput
      {
        message = "Just testing commit update...",
        streamId = streamId,
        id = commitId,
      };

      var res = await client.CommitUpdate(commitInput);
      Assert.True(res);

      await Task.Run(() => {
        Thread.Sleep(100); //let client catch-up
        Assert.NotNull(CommitUpdatedInfo);
        Assert.AreEqual(commitInput.message, CommitUpdatedInfo.message);
      });
    }

    private void Client_OnCommitUpdated(object sender, CommitInfo e)
    {
      CommitUpdatedInfo = e;
    }

    [Test, Order(3)]
    public async Task SubscribeCommitDeleted()
    {
      client.SubscribeCommitDeleted(streamId);
      client.OnCommitDeleted += Client_OnCommitDeleted;

      Thread.Sleep(100); //let server catch-up

      var commitInput = new CommitDeleteInput
      {
        streamId = streamId,
        id = commitId,
      };

      var res = await client.CommitDelete(commitInput);
      Assert.True(res);

      await Task.Run(() => {
        Thread.Sleep(100); //let client catch-up
        Assert.NotNull(CommitDeletedInfo);
        Assert.AreEqual(commitId, CommitDeletedInfo.id);
      });
    }

    private void Client_OnCommitDeleted(object sender, CommitInfo e)
    {
      CommitDeletedInfo = e;
    }

  }
}
