using System.Reflection;
using Npgsql;
using PointOfSale.API.Services.Payments;

namespace PointOfSale.Tests.Payments;

public sealed class ManualWalletAdmissionRetryTests
{
    [Fact]
    public async Task RolledBackSerializationFailure_RetriesSameOperation_BoundedAndNoOtherFailuresRetried()
    {
        var attempts=0;var retries=0;
        var result=await Execute(async()=>{await Task.Yield();if(++attempts<3)throw Serialization();return 42;},(_,_)=>retries++);
        Assert.Equal(42,result);Assert.Equal(3,attempts);Assert.Equal(2,retries);
        attempts=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Execute<int>(()=>{attempts++;throw new InvalidOperationException("business rejection");},(_,_)=>{}));
        Assert.Equal(1,attempts);
        attempts=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Execute<int>(()=>{attempts++;throw Serialization();},(_,_)=>{}));
        Assert.Equal(3,attempts);
    }

    [Fact]
    public async Task CancelledAdmission_DoesNotStartAnotherAttempt()
    {
        using var cts=new CancellationTokenSource();cts.Cancel();var attempts=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Execute(()=>Task.FromResult(++attempts),(_,_)=>{},cts.Token));
        Assert.Equal(0,attempts);
    }

    private static InvalidOperationException Serialization()=>new("transient wrapper",new PostgresException("serialization", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure));
    private static Task<T> Execute<T>(Func<Task<T>> attempt,Action<Exception,int> retry,CancellationToken ct=default)=>
        (Task<T>)typeof(PaymentService).Assembly.GetType("PointOfSale.API.Services.Payments.ManualWalletAdmissionRetry")!
            .GetMethod("ExecuteAsync",BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(typeof(T)).Invoke(null,[attempt,retry,ct])!;
}
