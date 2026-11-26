using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using UI.Services;

namespace UI.Views
{
    /// <summary>Xem và xử lý mã test: hàng đợi mã đã quét, mã đang gắn với từng khuôn, xác nhận sau khi app tắt.</summary>
    public partial class TestJobsWindow : Window
    {
        private readonly TestJobCoordinator _jobs;

        private sealed record JobRow(string Fixture, string Code, string Step, string Result, string At, string Note)
        {
            public int FixtureNo { get; init; }
        }

        private sealed record QueueRow(int Index, string Code, string At, string Status);

        public TestJobsWindow(TestJobCoordinator jobs)
        {
            InitializeComponent();
            _jobs = jobs;
            _jobs.Changed += OnChanged;
            Closed += (_, _) => _jobs.Changed -= OnChanged;
            Refresh();
        }

        private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

        private void Refresh()
        {
            JobsGrid.ItemsSource = _jobs.JobsSnapshot().Select(j => new JobRow(
                j.FixtureNo.ToString("00"), j.Code, StepText(j.Step),
                j.Result == Communication.Fixtures.SlotState.Unknown ? "" : j.Result.ToString().ToUpperInvariant(),
                j.At.ToString("HH:mm:ss"), j.Note)
            { FixtureNo = j.FixtureNo }).ToList();

            int i = 0;
            QueueGrid.ItemsSource = _jobs.QueueSnapshot().Select(q => new QueueRow(
                ++i, q.Code, q.At.ToString("HH:mm:ss"), q.Confirmed ? "Sẵn sàng" : "Chờ xác nhận (đọc từ file)")).ToList();

            HintText.Text = _jobs.HasUnconfirmed ? "Có mã chờ xác nhận: chưa gắn được vào khuôn" : "";
        }

        private static string StepText(JobStep s) => s switch
        {
            JobStep.Closed => "Đã đóng, gửi START",
            JobStep.Started => "Đang test",
            JobStep.Done => "Có kết quả",
            JobStep.NeedsCheck => "Cần kiểm tra",
            _ => s.ToString()
        };

        private void ReturnJob_Click(object sender, RoutedEventArgs e)
        {
            if (JobsGrid.SelectedItem is JobRow row) _jobs.ReturnToQueue(row.FixtureNo);
        }

        private void ClearJob_Click(object sender, RoutedEventArgs e)
        {
            if (JobsGrid.SelectedItem is JobRow row) _jobs.ClearJob(row.FixtureNo);
        }

        private void ConfirmAll_Click(object sender, RoutedEventArgs e) => _jobs.ConfirmAll();

        private void RemoveQueued_Click(object sender, RoutedEventArgs e)
        {
            if (QueueGrid.SelectedItem is QueueRow row) _jobs.RemoveQueued(row.Code);
        }
    }
}
