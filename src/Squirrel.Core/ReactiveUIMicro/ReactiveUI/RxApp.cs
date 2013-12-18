﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Diagnostics.Contracts;
using System.Linq;      
using System.Linq.Expressions;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;

using System.Threading.Tasks;
using System.Reactive.Threading.Tasks;

#if SILVERLIGHT
using System.Windows;
#endif

namespace ReactiveUIMicro
{
    /*
     * N.B. Why we have this evil global class
     * 
     * In a WPF or Silverlight application, most commands must have the Dispatcher 
     * scheduler set, because notifications will end up being run on another thread;
     * this happens most often in a CanExecute observable. Unfortunately, in a Unit
     * Test framework, while the MS Test Unit runner will *set* the Dispatcher (so
     * we can't even use the lack of its presence to determine whether we're in a
     * test runner or not), none of the items queued to it will ever be executed 
     * during the unit test.
     * 
     * Initially, I tried to plumb the ability to set the scheduler throughout the
     * classes, but when you start building applications on top of that, having to
     * have *every single* class have a default Scheduler property is really 
     * irritating, with either default making life difficult.
     */

    public static class RxApp
    {
        static RxApp()
        {
            // Default name for the field backing the "Foo" property => "_Foo"
            // This is used for ReactiveObject's RaiseAndSetIfChanged mixin
            GetFieldNameForPropertyNameFunc = new Func<string,string>(x => "_" + x);

#if WINDOWS_PHONE
            TaskpoolScheduler = new EventLoopScheduler();
#elif SILVERLIGHT || DOTNETISOLDANDSAD
            TaskpoolScheduler = Scheduler.ThreadPool;
#elif WINRT            
            TaskpoolScheduler = System.Reactive.Concurrency.ThreadPoolScheduler.Default;
#else
            TaskpoolScheduler = TaskPoolScheduler.Default;
#endif

            DefaultExceptionHandler = Observer.Create<Exception>(ex => 
                RxApp.DeferredScheduler.Schedule(() => {
                    throw new Exception("An exception occurred on an object that would destabilize ReactiveUI. To prevent this, Subscribe to the ThrownExceptions property of your objects", ex);
                }));

            LoggerFactory = t => new NullLogger();

            if (InUnitTestRunner()) {
                Console.Error.WriteLine("*** Detected Unit Test Runner, setting Scheduler to Immediate ***");
                Console.Error.WriteLine("If we are not actually in a test runner, please file a bug\n");
                DeferredScheduler = Scheduler.Immediate;
            }

            if (DeferredScheduler == null) {
                LogHost.Default.Error("*** ReactiveUI.Xaml DLL reference not added - using Event Loop *** ");
                LogHost.Default.Error("Add a reference to ReactiveUI.Xaml if you're using WPF / SL5 / WP7 / WinRT");
                LogHost.Default.Error("or consider explicitly setting RxApp.DeferredScheduler if not");
                RxApp.DeferredScheduler = new EventLoopScheduler();
            }
        }

        [ThreadStatic] static IScheduler _UnitTestDeferredScheduler;
        static IScheduler _DeferredScheduler;

        /// <summary>
        /// DeferredScheduler is the scheduler used to schedule work items that
        /// should be run "on the UI thread". In normal mode, this will be
        /// DispatcherScheduler, and in Unit Test mode this will be Immediate,
        /// to simplify writing common unit tests.
        /// </summary>
        public static IScheduler DeferredScheduler {
            get { return _UnitTestDeferredScheduler ?? _DeferredScheduler; }
            set {
                // N.B. The ThreadStatic dance here is for the unit test case -
                // often, each test will override DeferredScheduler with their
                // own TestScheduler, and if this wasn't ThreadStatic, they would
                // stomp on each other, causing test cases to randomly fail,
                // then pass when you rerun them.
                if (InUnitTestRunner()) {
                    _UnitTestDeferredScheduler = value;
                    _DeferredScheduler = _DeferredScheduler ?? value;
                } else {
                    _DeferredScheduler = value;
                }
            }
        }

        [ThreadStatic] static IScheduler _UnitTestTaskpoolScheduler;
        static IScheduler _TaskpoolScheduler;

        /// <summary>
        /// TaskpoolScheduler is the scheduler used to schedule work items to
        /// run in a background thread. In both modes, this will run on the TPL
        /// Task Pool (or the normal Threadpool on Silverlight).
        /// </summary>
        public static IScheduler TaskpoolScheduler {
            get { return _UnitTestTaskpoolScheduler ?? _TaskpoolScheduler; }
            set {
                if (InUnitTestRunner()) {
                    _UnitTestTaskpoolScheduler = value;
                    _TaskpoolScheduler = _TaskpoolScheduler ?? value;
                } else {
                    _TaskpoolScheduler = value;
                }
            }
        }

        /// <summary>
        /// Set this property to implement a custom logger provider - the
        /// string parameter is the 'prefix' (usually the class name of the log
        /// entry)
        /// </summary>

        static Func<Type, IRxUILogger> _LoggerFactory;
        static public readonly Subject<Unit> _LoggerFactoryChanged = new Subject<Unit>();
        public static Func<Type, IRxUILogger> LoggerFactory {
            get { return _LoggerFactory; }
            set { _LoggerFactory = value; _LoggerFactoryChanged.OnNext(Unit.Default); }
        }

        /// <summary>
        /// Set this property to override the default field naming convention
        /// of "_PropertyName" with a custom one.
        /// </summary>
        public static Func<string, string> GetFieldNameForPropertyNameFunc { get; set; }

        /// <summary>
        /// This method allows you to override the return value of 
        /// RxApp.InUnitTestRunner - a null value means that InUnitTestRunner
        /// will determine this using its normal logic.
        /// </summary>
        public static bool? InUnitTestRunnerOverride { get; set; }

        /// <summary>
        /// This Observer is signalled whenever an object that has a 
        /// ThrownExceptions property doesn't Subscribe to that Observable. Use
        /// Observer.Create to set up what will happen - the default is to crash
        /// the application with an error message.
        /// </summary>
        public static IObserver<Exception> DefaultExceptionHandler { get; set; }

        /// <summary>
        /// InUnitTestRunner attempts to determine heuristically if the current
        /// application is running in a unit test framework.
        /// </summary>
        /// <returns>True if we have determined that a unit test framework is
        /// currently running.</returns>
        public static bool InUnitTestRunner()
        {
            if (InUnitTestRunnerOverride.HasValue) {
                return InUnitTestRunnerOverride.Value;
            }

            // XXX: This is hacky and evil, but I can't think of any better way
            // to do this
            string[] testAssemblies = new[] {
                "CSUNIT",
                "NUNIT",
                "XUNIT",
                "MBUNIT",
                "TESTDRIVEN",
                "QUALITYTOOLS.TIPS.UNITTEST.ADAPTER",
                "QUALITYTOOLS.UNITTESTING.SILVERLIGHT",
                "PEX",
                "MSBUILD",
                "NBEHAVE",
            };

            string[] designEnvironments = new[] {
                "BLEND.EXE",
                "MONODEVELOP",
                "SHARPDEVELOP.EXE",
            };

#if SILVERLIGHT
            // NB: Deployment.Current.Parts throws an exception when accessed in Blend
            try {
                var ret = Deployment.Current.Parts.Any(x =>
                    testAssemblies.Any(name => x.Source.ToUpperInvariant().Contains(name)));

                if (ret) {
                    return ret;
                }
            } catch(Exception) {
                return true;
            }

            try {
                if (Application.Current.RootVisual != null && System.ComponentModel.DesignerProperties.GetIsInDesignMode(Application.Current.RootVisual)) {
                    return false;
                }
            } catch {
                return true;
            }

            return false;
#elif WINRT
            // NB: We have no way to detect if we're in design mode in WinRT.
            return false;
#else
            // Try to detect whether we're in design mode - bonus points, 
            // without access to any WPF references :-/
            var entry = Assembly.GetEntryAssembly();
            var exeName = (entry != null ? entry.Location.ToUpperInvariant() : "");
            if (designEnvironments.Any(x => x.Contains(exeName))) {
                return true;
            }

            return AppDomain.CurrentDomain.GetAssemblies().Any(x =>
                testAssemblies.Any(name => x.FullName.ToUpperInvariant().Contains(name)));
#endif
        }

        /// <summary>
        /// GetFieldNameForProperty returns the corresponding backing field name
        /// for a given property name, using the convention specified in
        /// GetFieldNameForPropertyNameFunc.
        /// </summary>
        /// <param name="propertyName">The name of the property whose backing
        /// field needs to be found.</param>
        /// <returns>The backing field name.</returns>
        public static string GetFieldNameForProperty(string propertyName)
        {
            return GetFieldNameForPropertyNameFunc(propertyName);
        }


        // 
        // Service Location
        //

        static Func<Type, string, object> _getService;
        static Func<Type, string, IEnumerable<object>> _getAllServices;
        static Action<Type, Type, string> _register;

        public static T GetService<T>(string key = null)
        {
            return (T)GetService(typeof(T), key);
        }

        public static object GetService(Type type, string key = null)
        {
            lock (_preregisteredTypes) {
                if (_preregisteredTypes.Count == 0) goto callSl;

                var k = Tuple.Create(type, key);
                if (!_preregisteredTypes.ContainsKey(k)) goto callSl;
                return Activator.CreateInstance(_preregisteredTypes[k].First());
            }
            
        callSl:
            var getService = _getService ??
                ((_, __) => { throw new Exception("You need to call RxApp.ConfigureServiceLocator to set up service location"); });
            return getService(type, key);
        }

        public static IEnumerable<T> GetAllServices<T>(string key = null)
        {
            return GetAllServices(typeof(T), key).Cast<T>().ToArray();
        }

        public static IEnumerable<object> GetAllServices(Type type, string key = null)
        {
            lock (_preregisteredTypes) {
                if (_preregisteredTypes.Count == 0) goto callSl;

                var k = Tuple.Create(type, key);
                if (!_preregisteredTypes.ContainsKey(k)) goto callSl;
                return _preregisteredTypes[k].Select(Activator.CreateInstance).ToArray();
            }
            
        callSl:
            var getAllServices = _getAllServices ??
                ((_,__) => { throw new Exception("You need to call RxApp.ConfigureServiceLocator to set up service location"); });
            return getAllServices(type, key).ToArray();
        }

        static readonly Dictionary<Tuple<Type, string>, List<Type>> _preregisteredTypes = new Dictionary<Tuple<Type, string>, List<Type>>();
        public static void Register(Type concreteType, Type interfaceType, string key = null)
        {
            // NB: This allows ReactiveUI itself (as well as other libraries) 
            // to register types before the actual service locator is set up,
            // or to serve as an ultra-crappy service locator if the app doesn't
            // use service location
            lock (_preregisteredTypes) {
                if (_register == null) {
                    var k = Tuple.Create(interfaceType, key);
                    if (!_preregisteredTypes.ContainsKey(k)) _preregisteredTypes[k] = new List<Type>();
                    _preregisteredTypes[k].Add(concreteType);
                } else {
                    _register(concreteType, interfaceType, key);
                }
            }
        }

        public static void ConfigureServiceLocator(
            Func<Type, string, object> getService, 
            Func<Type, string, IEnumerable<object>> getAllServices,
            Action<Type, Type, string> register)
        {
            if (getService == null || getAllServices == null || register == null) {
                throw new ArgumentException("Both getService and getAllServices must be implemented");
            }

            _getService = getService;
            _getAllServices = getAllServices;
            _register = register;

            // Empty out the types that were registered before service location
            // was set up.
            lock (_preregisteredTypes) {
                _preregisteredTypes.Keys
                    .SelectMany(x => _preregisteredTypes[x]
                        .Select(v => Tuple.Create(v, x.Item1, x.Item2)))
                    .ForEach(x => _register(x.Item1, x.Item2, x.Item3));
            }
        }

        public static bool IsServiceLocationConfigured()
        {
            return _getService != null && _getAllServices != null;
        }

        public static void ConfigureFileLogging(string appName = "Squirrel")
        {
            LoggerFactory = _ => new FileLogger(appName) { Level = LogLevel.Info };
        }
    }
}

// vim: tw=120 ts=4 sw=4 et :
